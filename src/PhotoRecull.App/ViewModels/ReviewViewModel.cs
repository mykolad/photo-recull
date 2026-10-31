using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public sealed partial class ReviewViewModel : ObservableObject
{
    private readonly Cleanup _cleanup;
    private readonly HashSet<string> _keptPaths;
    private readonly List<ReviewTileViewModel> _candidates;
    private readonly List<ReviewTileViewModel> _timeline;
    private readonly List<ReviewTileViewModel> _skipped;
    // Bursts by their newest frame, so they stay open when the filter or time zone changes.
    private readonly HashSet<string> _expandedBursts = [];
    private readonly Dictionary<ReviewTileViewModel, ReviewStackViewModel> _stackOfTile = [];
    private List<ReviewTileViewModel> _shownInOrder = [];

    public ReviewViewModel(Cleanup cleanup, IReadOnlyList<TargetScanResult> scanResults, IReadOnlyList<TargetSettings> targets)
    {
        _cleanup = cleanup;
        _keptPaths = AppServices.Database.LoadKeptPhotoPaths(cleanup.Id);
        Targets = targets;

        IEnumerable<ReviewCopy> Copies(ReviewTileKind kind, Func<MatchResult, List<PhotoFile>> photos) =>
            scanResults
                .Zip(targets)
                .SelectMany(pair => photos(pair.First.Match).Select(photo => new ReviewCopy(photo, pair.Second, kind)));

        // A photo on several targets is one tile; matched and unmatched copies are
        // grouped together, since one target can keep a photo another removes.
        List<ReviewTileViewModel> Tiles(IEnumerable<ReviewCopy> copies) =>
            TargetCopies.Group(copies.OrderBy(c => targets.ToList().IndexOf(c.Target)).ToList(), c => c.Photo, c => c.Target)
                .Select(group => new ReviewTileViewModel(
                    group, group.Any(c => c.Kind == ReviewTileKind.Candidate && _keptPaths.Contains(c.Photo.Path)),
                    ToggleKeep, OpenPreview, () => IsActionRunning))
                .ToList();

        _timeline = Tiles([.. Copies(ReviewTileKind.Candidate, m => m.Unmatched), .. Copies(ReviewTileKind.Match, m => m.Matched)]);
        _candidates = _timeline.Where(t => t.CanBeKept).ToList();
        _skipped = Tiles(Copies(ReviewTileKind.Skipped, m => m.Skipped));
        var settings = AppServices.Database.LoadSettings();
        ShowTimesInUtc = settings.ShowCaptureTimesInUtc;
        Filter = settings.ReviewFilter;
        ShowTab(ReviewTab.Timeline);
    }

    public IReadOnlyList<TargetSettings> Targets { get; }

    public ObservableCollection<ReviewDayViewModel> Days { get; } = [];

    public int TimelineCount => _timeline.Count;
    public int SkippedCount => _skipped.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabExplanation), nameof(IsTimelineTab))]
    public partial ReviewTab SelectedTab { get; set; }

    public bool IsTimelineTab => SelectedTab == ReviewTab.Timeline;

    public string TabExplanation => SelectedTab switch
    {
        ReviewTab.Timeline => "Newest first. Photos with no match in Google Photos are dimmed and marked to remove. Bursts are stacked: click one to see its frames.",
        _ => "Not from an iPhone or without a capture time. PhotoRecull never moves or deletes these.",
    };

    public bool HasNothingShown => Days.Count == 0;

    public string NothingShownText => (SelectedTab, Filter) switch
    {
        (ReviewTab.Skipped, _) => "Nothing was skipped.",
        (_, ReviewFilter.OnlyToRemove) => "Nothing to remove: every photo matched one in Google Photos.",
        (_, ReviewFilter.HideToRemove) => "Every photo in this range is marked to remove. Show all photos to review them.",
        _ => "No photos in this range.",
    };

    [ObservableProperty]
    public partial bool ShowTimesInUtc { get; set; }

    public int TimeModeIndex
    {
        get => ShowTimesInUtc ? 1 : 0;
        set
        {
            if ((value == 1) == ShowTimesInUtc)
                return;
            ShowTimesInUtc = value == 1;
            AppServices.Database.SaveSettings(AppServices.Database.LoadSettings() with { ShowCaptureTimesInUtc = ShowTimesInUtc });
            ShowTab(SelectedTab);
        }
    }

    public string LocalTimeLabel
    {
        get
        {
            var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            return $"Local time (UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset:h\\:mm})".Replace(":00)", ")");
        }
    }

    [ObservableProperty]
    public partial ReviewFilter Filter { get; set; }

    public int FilterIndex
    {
        get => (int)Filter;
        set
        {
            if (value < 0 || value == (int)Filter)
                return;
            Filter = (ReviewFilter)value;
            AppServices.Database.SaveSettings(AppServices.Database.LoadSettings() with { ReviewFilter = Filter });
            ShowTab(SelectedTab);
        }
    }

    // ---------------------------------------------------------------- tabs and grouping

    [RelayCommand]
    private void ShowTimeline() => ShowTab(ReviewTab.Timeline);

    [RelayCommand]
    private void ShowSkipped() => ShowTab(ReviewTab.Skipped);

    private void ShowTab(ReviewTab tab)
    {
        SelectedTab = tab;
        var tiles = tab == ReviewTab.Timeline ? _timeline : _skipped;
        var filter = tab == ReviewTab.Timeline ? Filter : ReviewFilter.All;
        foreach (var tile in tiles)
            tile.ShowTimesInUtc = ShowTimesInUtc;

        // By instance: targets with overlapping folders can each report the same
        // file, as equal records, and each copy has a tile of its own.
        var tileByPhoto = tiles.ToDictionary<ReviewTileViewModel, PhotoFile>(t => t.Photo, ReferenceEqualityComparer.Instance);
        var zone = ShowTimesInUtc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;

        Days.Clear();
        _stackOfTile.Clear();
        var shownInOrder = new List<ReviewTileViewModel>();
        foreach (var day in ReviewTimeline.NewestFirst(tiles.Select(t => t.Photo), p => tileByPhoto[p].Mark, filter, zone))
        {
            var stacks = new List<ReviewStackViewModel>();
            foreach (var group in day.Stacks)
            {
                var frames = group.Frames.Select(p => tileByPhoto[p]).ToList();
                var shown = group.Shown.Select(p => tileByPhoto[p]).ToList();
                for (int i = 0; i < frames.Count; i++)
                    frames[i].BurstPositionText = group.IsBurst ? $"Frame {frames.Count - i} of {frames.Count}, within {group.SpanSeconds} s" : "No";
                var stack = new ReviewStackViewModel(frames, shown, group.SpanSeconds, ToggleExpanded, OpenPreview)
                {
                    IsExpanded = group.IsBurst && _expandedBursts.Contains(frames[0].FullPath),
                };
                foreach (var frame in frames)
                    _stackOfTile[frame] = stack;
                shownInOrder.AddRange(shown);
                stacks.Add(stack);
            }

            var dayLabel = day.Day is { } d
                ? d.ToDateTime(TimeOnly.MinValue).ToString("ddd d MMM", CultureInfo.CurrentCulture)
                : "No capture time";
            Days.Add(new ReviewDayViewModel(dayLabel, day.Photos.Select(p => tileByPhoto[p]).ToList(), stacks,
                countsWhatStays: tab == ReviewTab.Timeline));
        }
        _shownInOrder = shownInOrder;
        OnPropertyChanged(nameof(HasNothingShown));
        OnPropertyChanged(nameof(NothingShownText));
    }

    private void ToggleExpanded(ReviewStackViewModel stack)
    {
        stack.IsExpanded = !stack.IsExpanded;
        if (stack.IsExpanded)
            _expandedBursts.Add(stack.Frames[0].FullPath);
        else
            _expandedBursts.Remove(stack.Frames[0].FullPath);
        stack.Day.RebuildSegments();
    }

    // ---------------------------------------------------------------- keep overrides

    // The files to stage or delete: every copy of every photo still marked to remove.
    public IReadOnlyList<ReviewCopy> CandidatesToActOn =>
        _candidates.Where(t => !t.IsKept).SelectMany(t => t.CandidateCopies).ToList();

    public int KeptByUserCount => _candidates.Count(t => t.IsKept);

    public string ActionSummaryTitle
    {
        get
        {
            int photos = _candidates.Count(t => !t.IsKept);
            var copies = CandidatesToActOn;
            // A Live Photo's video is a file of its own, staged or deleted with its photo.
            int files = copies.Sum(c => c.Photo.LivePhotoVideoPath is null ? 1 : 2);
            var fileCount = files == photos ? "" : $" · {files:N0} files";
            return $"{photos:N0} photo{(photos == 1 ? "" : "s")} to stage or delete{fileCount} · {FileSizeText.Format(copies.Sum(c => c.Photo.SizeBytes))}";
        }
    }

    public string ActionSummaryDetail
    {
        get
        {
            var perTarget = string.Join(" · ", CandidatesToActOn
                .GroupBy(c => SyncTargets.DisplayName(c.Target.Kind))
                .Select(g => $"{g.Key} {g.Count():N0}"));
            var kept = KeptByUserCount;
            return kept > 0
                ? $"{perTarget} · {kept:N0} kept by you, excluded"
                : $"{perTarget}{(perTarget.Length > 0 ? " · " : "")}Press Keep on any photo you forgot to pick";
        }
    }

    public bool HasCandidatesToActOn => CandidatesToActOn.Count > 0;

    public bool HasNothingToActOn => !HasCandidatesToActOn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleKeepInPreviewCommand))]
    public partial bool IsActionRunning { get; set; }

    partial void OnIsActionRunningChanged(bool value)
    {
        foreach (var tile in _candidates)
            tile.ToggleKeepCommand.NotifyCanExecuteChanged();
    }

    // Keeping a photo doesn't re-apply the filter, so it stays where it is
    // (and can be undone) until the filter changes.
    private void ToggleKeep(ReviewTileViewModel tile)
    {
        if (!tile.CanBeKept || IsActionRunning)
            return;
        tile.IsKept = !tile.IsKept;
        foreach (var copy in tile.CandidateCopies)
            AppServices.Database.SetPhotoKept(_cleanup.Id, copy.Photo.Path, tile.IsKept);
        if (_stackOfTile.TryGetValue(tile, out var stack))
        {
            stack.Refresh();
            stack.Day.Refresh();
        }
        OnPropertyChanged(nameof(ActionSummaryTitle));
        OnPropertyChanged(nameof(ActionSummaryDetail));
        OnPropertyChanged(nameof(HasCandidatesToActOn));
        OnPropertyChanged(nameof(HasNothingToActOn));
    }

    // ---------------------------------------------------------------- export

    [RelayCommand]
    private async Task ExportCandidatesAsync()
    {
        var path = await Pickers.PickSaveFileAsync($"candidates_{_cleanup.From:yyyy-MM-dd}_{_cleanup.To:yyyy-MM-dd}", ".csv", "CSV file");
        if (path is null)
            return;
        try
        {
            CandidateCsv.WriteNewestFirst(path, CandidatesToActOn.Select(c => c.Photo));
            ShellPage.Current?.ShowToast($"Exported {CandidatesToActOn.Count:N0} candidates to {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShellPage.Current?.ShowToast($"Couldn't export the list: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    // ---------------------------------------------------------------- preview (lightbox)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewOpen))]
    public partial ReviewTileViewModel? PreviewTile { get; set; }

    public bool IsPreviewOpen => PreviewTile is not null;

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.ImageSource? PreviewImage { get; set; }

    public string PreviewStatusText => PreviewTile?.Mark switch
    {
        ReviewMark.ToRemove when PreviewTile.Copies.Any(c => c.Kind == ReviewTileKind.Match) => PreviewTile.MarkText,
        ReviewMark.ToRemove => "To remove · no match in Google Photos",
        ReviewMark.KeptByYou => "Kept by you · not staged or deleted",
        _ when PreviewTile?.Kind == ReviewTileKind.Skipped => "Skipped · never moved or deleted",
        _ => "Stays · matched a picked photo",
    };

    public bool IsPreviewMarkedToRemove => PreviewTile?.IsMarkedToRemove == true;

    // The arrows walk through what the timeline shows, frames of collapsed
    // bursts included, in timeline order.
    public string PreviewPositionText => PreviewTile is null ? "" : $"{_shownInOrder.IndexOf(PreviewTile) + 1} of {_shownInOrder.Count}";

    private void OpenPreview(ReviewTileViewModel tile) => ShowPreview(tile);

    [RelayCommand]
    private void ClosePreview() => PreviewTile = null;

    [RelayCommand]
    private void ShowNextPreview() => MovePreview(+1);

    [RelayCommand]
    private void ShowPreviousPreview() => MovePreview(-1);

    [RelayCommand(CanExecute = nameof(CanToggleKeepInPreview))]
    private void ToggleKeepInPreview()
    {
        if (PreviewTile is { CanBeKept: true } tile)
        {
            ToggleKeep(tile);
            OnPropertyChanged(nameof(PreviewStatusText));
            OnPropertyChanged(nameof(IsPreviewMarkedToRemove));
        }
    }

    private bool CanToggleKeepInPreview() => !IsActionRunning;

    [RelayCommand]
    private void OpenOriginalInPreview() => PreviewTile?.OpenOriginalCommand.Execute(null);

    private void MovePreview(int step)
    {
        if (PreviewTile is null || _shownInOrder.Count == 0)
            return;
        int index = Math.Clamp(_shownInOrder.IndexOf(PreviewTile) + step, 0, _shownInOrder.Count - 1);
        ShowPreview(_shownInOrder[index]);
    }

    private async void ShowPreview(ReviewTileViewModel tile)
    {
        PreviewTile = tile;
        PreviewImage = tile.Thumbnail;
        OnPropertyChanged(nameof(PreviewStatusText));
        OnPropertyChanged(nameof(IsPreviewMarkedToRemove));
        OnPropertyChanged(nameof(PreviewPositionText));
        var fullSize = await Thumbnails.LoadAsync(tile.FullPath, ReviewTileViewModel.PreviewPixels);
        if (PreviewTile == tile && fullSize is not null)
            PreviewImage = fullSize;
    }
}
