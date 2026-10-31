using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

// A Staged cleanup's photos waiting in their stage folders: delete them for good,
// or move any back to keep them. The cleanup is Done once none are left.
public sealed partial class StagedReviewViewModel : ObservableObject
{
    private Cleanup _cleanup;

    public StagedReviewViewModel(Guid cleanupId)
    {
        _cleanup = AppServices.Database.LoadCleanups().First(c => c.Id == cleanupId);
        LoadStagedPhotos();
    }

    public string Title => DateRangeLabel.Format(_cleanup.From, _cleanup.To);

    public ObservableCollection<StagedTileViewModel> Photos { get; } = [];

    public bool HasPhotos => Photos.Count > 0;

    public string Summary => Photos.Count == 0
        ? "No staged photos are left."
        : $"{PhotoCount(Photos.Count)} waiting · {FileSizeText.Format(Photos.Sum(p => p.Photo.SizeBytes))} · "
            + string.Join(", ", Photos.Select(p => SyncTargets.DisplayName(p.Photo.Target.Kind)).Distinct());

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveAllBackCommand))]
    public partial bool IsActing { get; set; }

    [ObservableProperty]
    public partial string? ResultMessage { get; set; }

    [ObservableProperty]
    public partial bool ResultHasFailures { get; set; }

    // Deleting from a stage folder on a network drive skips the Windows Recycle Bin.
    public bool DeletesArePermanent => Photos.Any(p => !FileActions.HasRecycleBin(p.Photo.StagedPath));

    public event EventHandler? AllHandled;

    private void LoadStagedPhotos()
    {
        Photos.Clear();
        foreach (var photo in StagedPhotos.StillStaged(AppServices.Database, _cleanup.Id))
            Photos.Add(new StagedTileViewModel(photo, MoveBack));
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(Summary));
        MoveAllBackCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task MoveAllBackAsync() => ActAsync(CleanupAction.MovedBack, Photos.Select(p => p.Photo).ToList());

    public Task DeleteAllAsync() => ActAsync(CleanupAction.Deleted, Photos.Select(p => p.Photo).ToList());

    private bool CanAct() => !IsActing && Photos.Count > 0;

    private async void MoveBack(StagedTileViewModel tile)
    {
        if (!IsActing)
            await ActAsync(CleanupAction.MovedBack, [tile.Photo]);
    }

    // Recorded like a stage or delete, photo by photo, so History shows it and a
    // crash midway is finished at the next start.
    private async Task ActAsync(CleanupAction action, List<StagedPhoto> photos)
    {
        if (photos.Count == 0)
            return;
        IsActing = true;
        AppServices.IsRunInProgress = true;
        try
        {
            var ranAt = DateTimeOffset.Now;
            var runs = await Task.Run(() => photos
                .GroupBy(p => p.Target)
                .Select(g =>
                {
                    var started = new CleanupRun(_cleanup.Id, ranAt, action, g.Key,
                        action == CleanupAction.MovedBack ? g.Key.FolderPath : null, 0, 0, 0);
                    long runId = AppServices.Database.StartRun(started);
                    void Record(RunFile file) => AppServices.Database.RecordRunFile(runId, file);
                    var result = action == CleanupAction.MovedBack
                        ? FileActions.MoveBack(g, Record)
                        : Delete(g.ToList(), Record);
                    return started with
                    {
                        Id = runId,
                        SucceededCount = result.SucceededCount,
                        FailedCount = result.Failures.Count,
                        FreedBytes = action == CleanupAction.Deleted ? result.SucceededBytes : 0,
                    };
                })
                .ToList());

            // Photos moved back are kept: running the cleanup again won't offer them.
            if (action == CleanupAction.MovedBack)
                foreach (var file in runs.SelectMany(r => AppServices.Database.LoadRunFiles(r.Id)).Where(f => f.Succeeded))
                    AppServices.Database.SetPhotoKept(_cleanup.Id, file.Destination!, isKept: true);

            var stillStaged = StagedPhotos.StillStaged(AppServices.Database, _cleanup.Id);
            int succeeded = runs.Sum(r => r.SucceededCount), failed = runs.Sum(r => r.FailedCount);
            _cleanup = _cleanup with
            {
                State = stillStaged.Count == 0 ? CleanupState.Done : CleanupState.Staged,
                RemovedCount = _cleanup.RemovedCount + (action == CleanupAction.Deleted ? succeeded : 0),
                KeptByUserCount = _cleanup.KeptByUserCount + (action == CleanupAction.MovedBack ? succeeded : 0),
                FreedBytes = _cleanup.FreedBytes + runs.Sum(r => r.FreedBytes),
                LastActivityAt = ranAt,
                HasRuns = true,
            };
            AppServices.Database.CompleteRunsWithCleanup(_cleanup, runs);

            var verb = action == CleanupAction.MovedBack ? "Moved back" : "Deleted";
            ResultHasFailures = failed > 0;
            ResultMessage = $"{verb} {PhotoCount(succeeded)}." + (failed > 0
                ? $" {PhotoCount(failed)} failed: {string.Join("; ", runs.SelectMany(r => AppServices.Database.LoadRunFiles(r.Id)).Where(f => !f.Succeeded).Select(f => $"{Path.GetFileName(f.Path)} ({f.FailureReason})").Take(3))}"
                : "");
            LoadStagedPhotos();
            if (stillStaged.Count == 0)
                AllHandled?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            AppServices.IsRunInProgress = false;
            IsActing = false;
            MoveAllBackCommand.NotifyCanExecuteChanged();
        }
    }

    private static FileActionResult Delete(List<StagedPhoto> photos, Action<RunFile> onPhotoDone)
    {
        var files = photos.Select(p => new PhotoFile(p.StagedPath, p.FileName, null, IsIphone: true, TimestampSource: null)
        {
            LivePhotoVideoPath = p.StagedVideoPath,
        }).ToList();
        return files.All(f => FileActions.HasRecycleBin(f.Path))
            ? FileActions.MoveToRecycleBin(files, onPhotoDone)
            : FileActions.DeletePermanently(files, onPhotoDone);
    }

    private static string PhotoCount(int count) => count == 1 ? "1 photo" : $"{count:N0} photos";
}

public sealed partial class StagedTileViewModel(StagedPhoto photo, Action<StagedTileViewModel> moveBack) : ObservableObject
{
    private bool _thumbnailRequested;
    private ImageSource? _thumbnail;

    public StagedPhoto Photo { get; } = photo;
    public string FileName => Photo.FileName;
    public string TargetGlyph => ShellPage.TargetGlyph(Photo.Target.Kind);
    // Below the target's folder, which the target's name stands for.
    public string OriginalFolder
    {
        get
        {
            var below = Path.GetRelativePath(Photo.Target.FolderPath, Path.GetDirectoryName(Photo.OriginalPath)!);
            var target = SyncTargets.DisplayName(Photo.Target.Kind);
            return below == "." || below.StartsWith("..", StringComparison.Ordinal) ? $"From {target}" : $"From {target} · {below}";
        }
    }
    public string Details => (Photo.StagedVideoPath is null ? "" : "Live Photo · ") + FileSizeText.Format(Photo.SizeBytes);

    // Decoded when the tile is first laid out, like the Review timeline's tiles.
    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested)
            {
                _thumbnailRequested = true;
                _ = LoadThumbnailAsync();
            }
            return _thumbnail;
        }
    }

    private async Task LoadThumbnailAsync()
    {
        _thumbnail = await Thumbnails.LoadAsync(Photo.StagedPath, ReviewTileViewModel.TileThumbnailPixels);
        OnPropertyChanged(nameof(Thumbnail));
    }

    [RelayCommand]
    private void MoveBack() => moveBack(this);

    [RelayCommand]
    private void OpenOriginal() => BrowserLauncher.Open(Photo.StagedPath);
}
