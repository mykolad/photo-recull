using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.Core.Library;

namespace PhotoRecull.App.ViewModels;

// One cell of the timeline: a single photo, or a burst stacked behind its cover
// like a stack in Google Photos. Frames are newest first; Shown are the frames
// the filter lets through.
public sealed partial class ReviewStackViewModel(
    List<ReviewTileViewModel> frames,
    List<ReviewTileViewModel> shown,
    int spanSeconds,
    Action<ReviewStackViewModel> toggleExpanded,
    Action<ReviewTileViewModel> openPreview) : ObservableObject
{
    private List<ReviewStackViewModel>? _frameCells;

    public List<ReviewTileViewModel> Frames { get; } = frames;
    public List<ReviewTileViewModel> Shown { get; } = shown;
    public ReviewDayViewModel Day { get; set; } = null!;

    public bool IsBurst => Frames.Count > 1;

    public ReviewTileViewModel Cover => ReviewTimeline.Cover(Shown, frame => frame.Mark);

    // Peeks out from behind the cover, so a burst reads as a stack. When some
    // frames stay and some go, the one behind is a frame that goes, dimmed and
    // outlined, so the cover that stays doesn't speak for the whole burst.
    public ReviewTileViewModel FrameBehindCover =>
        Shown.FirstOrDefault(f => f != Cover && f.IsMarkedToRemove) ?? Shown.FirstOrDefault(f => f != Cover) ?? Cover;

    public bool ShowsVideoBadge => !IsBurst && Cover.IsVideo;

    public string FrameCountText => Frames.Count.ToString("N0");

    private int StayingCount => Frames.Count(f => f.Mark != ReviewMark.ToRemove);
    private int ToRemoveCount => Frames.Count(f => f.Mark == ReviewMark.ToRemove);

    public bool IsMixedBurst => IsBurst && StayingCount > 0 && ToRemoveCount > 0;
    public bool ShowsFrameCount => IsBurst && !IsMixedBurst;
    public string StayingCountText => StayingCount.ToString("N0");
    public string ToRemoveCountText => ToRemoveCount.ToString("N0");

    public string Title => IsBurst ? $"Burst · {Frames.Count} frames within {spanSeconds} s" : Cover.FileName;

    public string Caption => ReviewTimeline.StackCaption(
        Frames.Count(f => f.Mark != ReviewMark.ToRemove), Frames.Count(f => f.Mark == ReviewMark.ToRemove), Shown.Count);

    public string Subtitle => IsBurst ? Caption : Cover.TimeLine;

    public string Header => $"{Title} · {Caption}";

    public string AutomationName => IsBurst ? $"{Title}, {Caption}. Show frames" : $"{Cover.FileName}, {Cover.MarkText}";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    // An expanded burst lays its frames out as cells of their own.
    public List<ReviewStackViewModel> FrameCells => _frameCells ??= Shown
        .Select(frame => new ReviewStackViewModel([frame], [frame], 0, toggleExpanded, openPreview) { Day = Day })
        .ToList();

    public void Refresh()
    {
        OnPropertyChanged(nameof(Cover));
        OnPropertyChanged(nameof(FrameBehindCover));
        OnPropertyChanged(nameof(ShowsVideoBadge));
        OnPropertyChanged(nameof(IsMixedBurst));
        OnPropertyChanged(nameof(ShowsFrameCount));
        OnPropertyChanged(nameof(StayingCountText));
        OnPropertyChanged(nameof(ToRemoveCountText));
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(AutomationName));
        foreach (var cell in _frameCells ?? [])
            cell.Refresh();
    }

    [RelayCommand]
    private void Activate()
    {
        if (IsBurst)
            toggleExpanded(this);
        else
            openPreview(Cover);
    }

    [RelayCommand]
    private void ToggleExpanded() => toggleExpanded(this);
}

// A run of cells that flow together, or one expanded burst on rows of its own.
public sealed class ReviewSegmentViewModel(ReviewStackViewModel? expandedBurst, List<ReviewStackViewModel> cells)
{
    public ReviewStackViewModel? ExpandedBurst { get; } = expandedBurst;
    public bool IsExpandedBurst => ExpandedBurst is not null;
    public List<ReviewStackViewModel> Cells { get; } = cells;
}

public sealed partial class ReviewDayViewModel : ObservableObject
{
    private readonly string _dayLabel;
    private readonly List<ReviewTileViewModel> _allPhotos;
    private readonly bool _countsWhatStays;

    public ReviewDayViewModel(string dayLabel, List<ReviewTileViewModel> allPhotos, List<ReviewStackViewModel> stacks, bool countsWhatStays)
    {
        _dayLabel = dayLabel;
        _allPhotos = allPhotos;
        _countsWhatStays = countsWhatStays;
        Stacks = stacks;
        foreach (var stack in stacks)
            stack.Day = this;
        RebuildSegments();
    }

    public List<ReviewStackViewModel> Stacks { get; }

    public ObservableCollection<ReviewSegmentViewModel> Segments { get; } = [];

    public string Header => _countsWhatStays
        ? $"{_dayLabel} · {ReviewTimeline.DayCaption(_allPhotos.Count(p => p.Mark != ReviewMark.ToRemove), _allPhotos.Count(p => p.Mark == ReviewMark.ToRemove))}"
        : $"{_dayLabel} · {_allPhotos.Count} photo{(_allPhotos.Count == 1 ? "" : "s")}";

    public void Refresh() => OnPropertyChanged(nameof(Header));

    public void RebuildSegments()
    {
        Segments.Clear();
        var run = new List<ReviewStackViewModel>();
        foreach (var stack in Stacks)
        {
            if (!stack.IsExpanded)
            {
                run.Add(stack);
                continue;
            }
            if (run.Count > 0)
                Segments.Add(new ReviewSegmentViewModel(null, run));
            run = [];
            Segments.Add(new ReviewSegmentViewModel(stack, stack.FrameCells));
        }
        if (run.Count > 0)
            Segments.Add(new ReviewSegmentViewModel(null, run));
    }
}
