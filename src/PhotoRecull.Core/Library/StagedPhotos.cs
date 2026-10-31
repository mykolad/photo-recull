using PhotoRecull.Core.Settings;
using PhotoRecull.Core.Storage;

namespace PhotoRecull.Core.Library;

// A photo waiting in a stage folder, and where it came from on its target.
public sealed record StagedPhoto(
    TargetSettings Target, string StagedPath, string OriginalPath, string? StagedVideoPath, string? OriginalVideoPath)
{
    public string FileName => Path.GetFileName(StagedPath);

    public long SizeBytes => SizeOrZero(StagedPath) + (StagedVideoPath is { } video ? SizeOrZero(video) : 0);

    private static long SizeOrZero(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}

// What's still staged, and the stage folders that couldn't be checked: a NAS
// that's off makes its staged photos look gone, which isn't the same as gone.
public sealed record StagedInventory(List<StagedPhoto> Photos, List<string> UnreachableFolders)
{
    public bool IsVerifiedEmpty => Photos.Count == 0 && UnreachableFolders.Count == 0;
}

public static class StagedPhotos
{
    public static StagedInventory Load(AppStateDatabase database, Guid cleanupId) =>
        StillStaged(database.LoadRunsNewestFirst()
            .Where(r => r.CleanupId == cleanupId && r.Action == CleanupAction.Staged)
            .Select(r => (r, database.LoadRunFiles(r.Id))));

    // The photos a cleanup's stage runs moved that are still in a stage folder: not
    // yet deleted, moved back, or moved away by hand.
    public static StagedInventory StillStaged(IEnumerable<(CleanupRun Run, List<RunFile> Files)> stageRuns)
    {
        var runs = stageRuns.ToList();
        var unreachable = runs
            .Select(r => r.Run.Destination)
            .OfType<string>()
            .Where(folder => !Directory.Exists(folder) && !IsReachable(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var photos = FromRecordedPhotos(runs.Where(r => r.Run.PhotosRecorded).ToList());
        // Runs saved before photos were recorded one by one: their stage folder is
        // all there is to go by, the original places rebuilt from its folders.
        foreach (var (run, _) in runs.Where(r => !r.Run.PhotosRecorded && r.Run.Status == RunStatus.Complete))
            if (run.Destination is { } stageFolder && Directory.Exists(stageFolder))
                photos.AddRange(FromStageFolder(run.Target, stageFolder));

        return new StagedInventory(
            photos.DistinctBy(p => p.StagedPath, StringComparer.OrdinalIgnoreCase).ToList(),
            unreachable);
    }

    // A photo's outcomes are put together across runs, newest last: a retry of a
    // Live Photo whose video was staged first records only the photo's move.
    private static List<StagedPhoto> FromRecordedPhotos(List<(CleanupRun Run, List<RunFile> Files)> runs)
    {
        var byOriginal = new Dictionary<string, (TargetSettings Target, string? Staged, string? VideoPath, string? StagedVideo)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (run, files) in runs.OrderBy(r => r.Run.RanAt).ThenBy(r => r.Run.Id))
        {
            foreach (var file in files)
            {
                var known = byOriginal.GetValueOrDefault(file.Path, (run.Target, null, null, null));
                byOriginal[file.Path] = (run.Target, file.Destination ?? known.Staged,
                    file.VideoPath ?? known.VideoPath, file.VideoDestination ?? known.StagedVideo);
            }
        }

        var photos = new List<StagedPhoto>();
        foreach (var (original, (target, staged, videoPath, stagedVideo)) in byOriginal)
        {
            bool stillStaged = staged is not null && File.Exists(staged);
            bool videoStaged = stagedVideo is not null && videoPath is not null && File.Exists(stagedVideo);
            if (stillStaged)
                photos.Add(new StagedPhoto(target, staged!, original, videoStaged ? stagedVideo : null, videoStaged ? videoPath : null));
            // The photo failed to stage, or was moved away by hand: its video is
            // still there, and goes back or is deleted on its own.
            else if (videoStaged)
                photos.Add(new StagedPhoto(target, stagedVideo!, videoPath!, null, null));
        }
        return photos;
    }

    private static IEnumerable<StagedPhoto> FromStageFolder(TargetSettings target, string stageFolder)
    {
        var files = Directory.EnumerateFiles(stageFolder, "*", SearchOption.AllDirectories)
            .Where(PhotoScanner.IsPhotoOrVideo)
            .ToList();
        var videoOfStill = PhotoScanner.LivePhotoVideoByStill(files);
        var pairedVideos = videoOfStill.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string OriginalOf(string staged) => Path.Combine(target.FolderPath, Path.GetRelativePath(stageFolder, staged));
        return files
            .Where(f => !pairedVideos.Contains(f))
            .Select(f => videoOfStill.TryGetValue(f, out var video)
                ? new StagedPhoto(target, f, OriginalOf(f), video, OriginalOf(video))
                : new StagedPhoto(target, f, OriginalOf(f), null, null));
    }

    // A missing stage folder on a drive or share that answers was removed (by hand,
    // once emptied); one on a drive or share that doesn't can't be checked.
    private static bool IsReachable(string folder)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(folder)) is { Length: > 0 } root && Directory.Exists(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
