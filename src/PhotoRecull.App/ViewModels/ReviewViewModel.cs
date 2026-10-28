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
    private readonly List<ReviewTileViewModel> _toRemove;
    private readonly List<ReviewTileViewModel> _toKeep;
    private readonly List<ReviewTileViewModel> _skipped;
    private List<ReviewTileViewModel> _currentTabTiles = [];

    public ReviewViewModel(Cleanup cleanup, IReadOnlyList<TargetScanResult> scanResults, IReadOnlyList<TargetSettings> targets)
    {
        _cleanup = cleanup;
        _keptPaths = AppServices.Database.LoadKeptPhotoPaths(cleanup.Id);
        Targets = targets;

        List<ReviewTileViewModel> Tiles(ReviewTab tab, Func<MatchResult, List<PhotoFile>> photos) =>
            scanResults
                .Zip(targets)
                .SelectMany(pair => photos(pair.First.Match).Select(photo => new ReviewTileViewModel(
                    photo, pair.Second, tab, tab == ReviewTab.ToRemove && _keptPaths.Contains(photo.Path),
                    ToggleKeep, OpenPreview)))
                .ToList();

        _toRemove = Tiles(ReviewTab.ToRemove, m => m.Unmatched);
        _toKeep = Tiles(ReviewTab.ToKeep, m => m.Matched);
        _skipped = Tiles(ReviewTab.Skipped, m => m.Skipped);
        ShowTimesInUtc = AppServices.Database.LoadSettings().ShowCaptureTimesInUtc;
        ShowTab(ReviewTab.ToRemove);
    }

    public IReadOnlyList<TargetSettings> Targets { get; }

    public ObservableCollection<ReviewDayViewModel> Days { get; } = [];

    public int ToRemoveCount => _toRemove.Count;
    public int ToKeepCount => _toKeep.Count;
    public int SkippedCount => _skipped.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabExplanation), nameof(IsToRemoveTab))]
    public partial ReviewTab SelectedTab { get; set; }

    public bool IsToRemoveTab => SelectedTab == ReviewTab.ToRemove;

    public string TabExplanation => SelectedTab switch
    {
        ReviewTab.ToRemove => "No match in Google Photos. Newest first, grouped by day, bursts together. Click a photo for a large preview.",
        ReviewTab.ToKeep => "Matched one-to-one within ±2 s of a picked photo. Spot-check these are photos you kept.",
        _ => "Not from an iPhone or without a capture time. PhotoRecull never moves or deletes these.",
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

    // ---------------------------------------------------------------- tabs and grouping

    [RelayCommand]
    private void ShowToRemove() => ShowTab(ReviewTab.ToRemove);

    [RelayCommand]
    private void ShowToKeep() => ShowTab(ReviewTab.ToKeep);

    [RelayCommand]
    private void ShowSkipped() => ShowTab(ReviewTab.Skipped);

    private void ShowTab(ReviewTab tab)
    {
        SelectedTab = tab;
        var tiles = tab switch
        {
            ReviewTab.ToRemove => _toRemove,
            ReviewTab.ToKeep => _toKeep,
            _ => _skipped,
        };
        foreach (var tile in tiles)
            tile.ShowTimesInUtc = ShowTimesInUtc;

        var tileByPhoto = tiles.ToDictionary(t => t.Photo);
        var zone = ShowTimesInUtc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;
        var noun = tab == ReviewTab.ToRemove ? "candidate" : "photo";

        Days.Clear();
        var ordered = new List<ReviewTileViewModel>();
        foreach (var day in ReviewGrouping.NewestFirstByDayWithBursts(tiles.Select(t => t.Photo), zone))
        {
            var segments = new List<ReviewSegmentViewModel>();
            var singles = new List<ReviewTileViewModel>();
            foreach (var group in day.Groups)
            {
                var groupTiles = group.Photos.Select(p => tileByPhoto[p]).ToList();
                ordered.AddRange(groupTiles);
                if (!group.IsBurst)
                {
                    singles.AddRange(groupTiles);
                    continue;
                }
                if (singles.Count > 0)
                    segments.Add(new ReviewSegmentViewModel(false, "", singles));
                singles = [];
                for (int i = 0; i < groupTiles.Count; i++)
                    groupTiles[i].BurstPositionText = $"Frame {groupTiles.Count - i} of {groupTiles.Count}, within {group.SpanSeconds} s";
                segments.Add(new ReviewSegmentViewModel(true,
                    $"Burst · {groupTiles.Count} frames within {group.SpanSeconds} s", groupTiles));
            }
            if (singles.Count > 0)
                segments.Add(new ReviewSegmentViewModel(false, "", singles));

            var dayLabel = day.Day is { } d
                ? d.ToDateTime(TimeOnly.MinValue).ToString("ddd d MMM", CultureInfo.CurrentCulture)
                : "No capture time";
            Days.Add(new ReviewDayViewModel($"{dayLabel} · {day.PhotoCount} {noun}{(day.PhotoCount == 1 ? "" : "s")}", segments));
        }
        _currentTabTiles = ordered;
    }

    // ---------------------------------------------------------------- keep overrides

    public IReadOnlyList<ReviewTileViewModel> CandidatesToActOn => _toRemove.Where(t => !t.IsKept).ToList();

    public int KeptByUserCount => _toRemove.Count(t => t.IsKept);

    public string ActionSummaryTitle
    {
        get
        {
            var candidates = CandidatesToActOn;
            return $"{candidates.Count:N0} photo{(candidates.Count == 1 ? "" : "s")} to stage or delete · {FileSizeText.Format(candidates.Sum(t => t.SizeBytes))}";
        }
    }

    public string ActionSummaryDetail
    {
        get
        {
            var perTarget = string.Join(" · ", CandidatesToActOn
                .GroupBy(t => t.TargetName)
                .Select(g => $"{g.Key} {g.Count():N0}"));
            var kept = KeptByUserCount;
            return kept > 0
                ? $"{perTarget} · {kept:N0} kept by you, excluded"
                : $"{perTarget}{(perTarget.Length > 0 ? " · " : "")}Press Keep on any photo you forgot to pick";
        }
    }

    public bool HasCandidatesToActOn => CandidatesToActOn.Count > 0;

    private void ToggleKeep(ReviewTileViewModel tile)
    {
        if (!tile.CanBeKept)
            return;
        tile.IsKept = !tile.IsKept;
        AppServices.Database.SetPhotoKept(_cleanup.Id, tile.FullPath, tile.IsKept);
        OnPropertyChanged(nameof(ActionSummaryTitle));
        OnPropertyChanged(nameof(ActionSummaryDetail));
        OnPropertyChanged(nameof(HasCandidatesToActOn));
    }

    // ---------------------------------------------------------------- export

    [RelayCommand]
    private async Task ExportCandidatesAsync()
    {
        var path = await Pickers.PickSaveFileAsync($"candidates_{_cleanup.From:yyyy-MM-dd}_{_cleanup.To:yyyy-MM-dd}", ".csv", "CSV file");
        if (path is null)
            return;
        CandidateCsv.WriteNewestFirst(path, CandidatesToActOn.Select(t => t.Photo));
        ShellPage.Current?.ShowToast($"Exported {CandidatesToActOn.Count:N0} candidates to {Path.GetFileName(path)}.");
    }

    // ---------------------------------------------------------------- preview (lightbox)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewOpen))]
    public partial ReviewTileViewModel? PreviewTile { get; set; }

    public bool IsPreviewOpen => PreviewTile is not null;

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.ImageSource? PreviewImage { get; set; }

    public string PreviewStatusText => PreviewTile?.Tab switch
    {
        ReviewTab.ToRemove => PreviewTile.IsKept ? "Kept by you · not staged or deleted" : "To remove · no match in Google Photos",
        ReviewTab.ToKeep => "To keep · matched a picked photo",
        _ => "Skipped · never moved or deleted",
    };

    public string PreviewPositionText => PreviewTile is null ? "" : $"{_currentTabTiles.IndexOf(PreviewTile) + 1} of {_currentTabTiles.Count}";

    private void OpenPreview(ReviewTileViewModel tile) => ShowPreview(tile);

    [RelayCommand]
    private void ClosePreview() => PreviewTile = null;

    [RelayCommand]
    private void ShowNextPreview() => MovePreview(+1);

    [RelayCommand]
    private void ShowPreviousPreview() => MovePreview(-1);

    [RelayCommand]
    private void ToggleKeepInPreview()
    {
        if (PreviewTile is { CanBeKept: true } tile)
        {
            ToggleKeep(tile);
            OnPropertyChanged(nameof(PreviewStatusText));
        }
    }

    [RelayCommand]
    private void OpenOriginalInPreview() => PreviewTile?.OpenOriginalCommand.Execute(null);

    private void MovePreview(int step)
    {
        if (PreviewTile is null || _currentTabTiles.Count == 0)
            return;
        int index = Math.Clamp(_currentTabTiles.IndexOf(PreviewTile) + step, 0, _currentTabTiles.Count - 1);
        ShowPreview(_currentTabTiles[index]);
    }

    private async void ShowPreview(ReviewTileViewModel tile)
    {
        PreviewTile = tile;
        PreviewImage = tile.Thumbnail;
        OnPropertyChanged(nameof(PreviewStatusText));
        OnPropertyChanged(nameof(PreviewPositionText));
        var fullSize = await Thumbnails.LoadAsync(tile.FullPath, ReviewTileViewModel.PreviewPixels);
        if (PreviewTile == tile && fullSize is not null)
            PreviewImage = fullSize;
    }
}
