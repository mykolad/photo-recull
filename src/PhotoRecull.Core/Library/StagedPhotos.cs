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

public static class StagedPhotos
{
    // The photos a cleanup's stage runs moved that are still in their stage
    // folder: not yet deleted, moved back, or moved away by hand. Runs from before
    // photos were recorded one by one are read from their stage folder instead,
    // each photo's original place rebuilt from its folders below the target's.
    public static List<StagedPhoto> StillStaged(AppStateDatabase database, Guid cleanupId) =>
        StillStaged(database.LoadRunsNewestFirst()
            .Where(r => r.CleanupId == cleanupId && r.Action == CleanupAction.Staged)
            .Select(r => (r, database.LoadRunFiles(r.Id))));

    public static List<StagedPhoto> StillStaged(IEnumerable<(CleanupRun Run, List<RunFile> Files)> stageRuns)
    {
        var photos = new List<StagedPhoto>();
        foreach (var (run, files) in stageRuns)
        {
            if (files.Count > 0)
            {
                photos.AddRange(files
                    .Where(f => f.Destination is not null || f.VideoDestination is not null)
                    .Select(f => f.Destination is { } stagedStill
                        ? new StagedPhoto(run.Target, stagedStill, f.Path, f.VideoDestination, f.VideoDestination is null ? null : f.VideoPath)
                        // Only the Live Photo's video moved before its photo failed.
                        : new StagedPhoto(run.Target, f.VideoDestination!, f.VideoPath!, null, null)));
            }
            else if (run.Destination is { } stageFolder && Directory.Exists(stageFolder))
            {
                photos.AddRange(Directory.EnumerateFiles(stageFolder, "*", SearchOption.AllDirectories)
                    .Where(PhotoScanner.IsPhotoOrVideo)
                    .Select(staged => new StagedPhoto(run.Target, staged,
                        Path.Combine(run.Target.FolderPath, Path.GetRelativePath(stageFolder, staged)), null, null)));
            }
        }
        return photos
            .Where(p => File.Exists(p.StagedPath))
            .DistinctBy(p => p.StagedPath, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.StagedVideoPath is { } video && !File.Exists(video) ? p with { StagedVideoPath = null, OriginalVideoPath = null } : p)
            .ToList();
    }
}
