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
        SettleIfNothingIsLeft(LoadStagedPhotos());
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
    public bool DeletesArePermanent => Photos.Any(p => !CanGoToRecycleBin(p.Photo));

    // Both parts of a Live Photo: a retry can stage its video and its photo on
    // different drives.
    private static bool CanGoToRecycleBin(StagedPhoto photo) =>
        FileActions.HasRecycleBin(photo.StagedPath)
        && (photo.StagedVideoPath is not { } video || FileActions.HasRecycleBin(video));

    public event EventHandler? AllHandled;

    // Stage folders that couldn't be checked (a NAS that's off): their photos
    // aren't listed, and the cleanup isn't taken as done.
    [ObservableProperty]
    public partial string? UnreachableMessage { get; set; }

    private StagedInventory LoadStagedPhotos()
    {
        var inventory = StagedPhotos.Load(AppServices.Database, _cleanup.Id);
        Photos.Clear();
        foreach (var photo in inventory.Photos)
            Photos.Add(new StagedTileViewModel(photo, MoveBack));
        UnreachableMessage = inventory.UnreachableFolders.Count == 0 ? null
            : $"Can't reach {string.Join(", ", inventory.UnreachableFolders)}. Its staged photos show here once it's back.";
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(Summary));
        MoveAllBackCommand.NotifyCanExecuteChanged();
        return inventory;
    }

    // Opening a Staged cleanup whose photos have all gone (handled by hand, or
    // by a run cut off after its last photo) settles it as Done.
    private void SettleIfNothingIsLeft(StagedInventory inventory)
    {
        if (_cleanup.State != CleanupState.Staged || !inventory.IsVerifiedEmpty)
            return;
        _cleanup = _cleanup with { State = CleanupState.Done, StagedCount = 0, LastActivityAt = DateTimeOffset.Now };
        AppServices.Database.SaveCleanup(_cleanup);
        ResultHasFailures = false;
        ResultMessage = "No staged photos are left, so this cleanup is now marked Done.";
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
                    var targetPhotos = g.ToList();
                    bool toRecycleBin = action == CleanupAction.Deleted && targetPhotos.All(CanGoToRecycleBin);
                    var started = new CleanupRun(_cleanup.Id, ranAt, action, g.Key,
                        action == CleanupAction.MovedBack ? g.Key.FolderPath : null, 0, 0, 0)
                    {
                        WentToRecycleBin = action == CleanupAction.Deleted ? toRecycleBin : null,
                    };
                    long runId = AppServices.Database.StartRun(started);
                    // A photo moved back is kept in the same step as it's recorded, so
                    // running the cleanup again won't offer it, even if this is cut off.
                    void Record(RunFile file) => AppServices.Database.RecordRunFile(runId, file,
                        keptFor: action == CleanupAction.MovedBack ? _cleanup.Id : null);
                    var result = action == CleanupAction.MovedBack
                        ? FileActions.MoveBack(targetPhotos, Record)
                        : Delete(targetPhotos, toRecycleBin, Record);
                    return started with
                    {
                        Id = runId,
                        SucceededCount = result.SucceededCount,
                        FailedCount = result.Failures.Count,
                        FreedBytes = action == CleanupAction.Deleted ? result.SucceededBytes : 0,
                    };
                })
                .ToList());

            var remaining = StagedPhotos.Load(AppServices.Database, _cleanup.Id);
            int succeeded = runs.Sum(r => r.SucceededCount), failed = runs.Sum(r => r.FailedCount);
            _cleanup = _cleanup with
            {
                State = remaining.IsVerifiedEmpty ? CleanupState.Done : CleanupState.Staged,
                StagedCount = remaining.Photos.Count,
                RemovedCount = _cleanup.RemovedCount + (action == CleanupAction.Deleted ? succeeded : 0),
                KeptByUserCount = _cleanup.KeptByUserCount + (action == CleanupAction.MovedBack ? succeeded : 0),
                FreedBytes = _cleanup.FreedBytes + runs.Sum(r => r.FreedBytes),
                LastActivityAt = ranAt,
                HasEverRun = true,
            };
            AppServices.Database.CompleteRunsWithCleanup(_cleanup, runs);

            var verb = action == CleanupAction.MovedBack ? "Moved back" : "Deleted";
            ResultHasFailures = failed > 0;
            ResultMessage = $"{verb} {PhotoCount(succeeded)}." + (failed > 0
                ? $" {PhotoCount(failed)} failed: {string.Join("; ", runs.SelectMany(r => AppServices.Database.LoadRunFiles(r.Id)).Where(f => !f.Succeeded).Select(f => $"{Path.GetFileName(f.Path)} ({f.FailureReason})").Take(3))}"
                : "");
            if (LoadStagedPhotos().IsVerifiedEmpty)
                AllHandled?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            AppServices.IsRunInProgress = false;
            IsActing = false;
            MoveAllBackCommand.NotifyCanExecuteChanged();
        }
    }

    private static FileActionResult Delete(List<StagedPhoto> photos, bool toRecycleBin, Action<RunFile> onPhotoDone)
    {
        var files = photos.Select(p => new PhotoFile(p.StagedPath, p.FileName, null, IsIphone: true, TimestampSource: null)
        {
            LivePhotoVideoPath = p.StagedVideoPath,
        }).ToList();
        return toRecycleBin
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
