using PhotoRecull.Core.Settings;
using PhotoRecull.Core.Storage;

namespace PhotoRecull.Core.Library;

// A photo waiting in a stage folder, and where it came from on its target.
public sealed record StagedPhoto(
    TargetSettings Target, string StagedPath, string OriginalPath, string? StagedVideoPath, string? OriginalVideoPath)
{
    public string FileName => Path.GetFileName(StagedPath);

    // Read once, with the inventory, in the background: a stage folder can be on a NAS.
    public long SizeBytes { get; init; }

    // For a Live Photo's video staged on its own: the photo it belongs to, which
    // moving the video back keeps too, so running the cleanup again won't offer
    // the pair (the scanner joins them again).
    public string? OriginalStillPath { get; init; }

    public StagedPhoto WithSizeRead() =>
        this with { SizeBytes = SizeOrZero(StagedPath) + (StagedVideoPath is { } video ? SizeOrZero(video) : 0) };

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
    public static StagedInventory Load(AppStateDatabase database, Guid cleanupId)
    {
        var everyStageRun = LoadEveryStageRun(database);
        return StillStaged(everyStageRun.Where(r => r.Run.CleanupId == cleanupId), everyStageRun);
    }

    public static List<(CleanupRun Run, List<RunFile> Files)> LoadEveryStageRun(AppStateDatabase database) =>
        database.LoadRunsNewestFirst()
            .Where(r => r.Action == CleanupAction.Staged)
            .Select(r => (r, database.LoadRunFiles(r.Id)))
            .ToList();

    // The photos a cleanup's stage runs moved that are still in a stage folder: not
    // yet deleted, moved back, or moved away by hand. A file only counts as gone
    // when the file system says it isn't there; one that can't be checked (no
    // access, a NAS that's off) puts its stage folder among the unreachable ones.
    // A stage path belongs to the newest run, of everyStageRun, that put a file
    // there: a photo moved back and staged again by another cleanup for the same
    // months lands on the same path, and the first cleanup mustn't delete it.
    public static StagedInventory StillStaged(
        IEnumerable<(CleanupRun Run, List<RunFile> Files)> stageRuns,
        IEnumerable<(CleanupRun Run, List<RunFile> Files)>? everyStageRun = null)
    {
        var runs = stageRuns.ToList();
        var ownRuns = runs.Select(r => r.Run.Id).ToHashSet();
        var newestRunOf = NewestRunByStagedPath(everyStageRun ?? runs);
        bool IsOwn(string path) => !newestRunOf.TryGetValue(path, out long runId) || ownRuns.Contains(runId);
        var unreachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var photos = FromRecordedPhotos(runs.Where(r => r.Run.PhotosRecorded).ToList(), IsOwn, unreachable);

        // Runs saved before photos were recorded one by one: their stage folder is
        // all there is to go by, the original places rebuilt from its folders.
        foreach (var (run, _) in runs.Where(r => !r.Run.PhotosRecorded && r.Run.Status == RunStatus.Complete))
        {
            if (run.Destination is not { } stageFolder)
                continue;
            switch (Check(stageFolder))
            {
                case PathCheck.Present:
                    try
                    {
                        photos.AddRange(FromStageFolder(run.Target, stageFolder));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        unreachable.Add(stageFolder);
                    }
                    break;
                case PathCheck.Unknown:
                    unreachable.Add(stageFolder);
                    break;
            }
        }

        return new StagedInventory(
            photos
                // The same file found both ways (an old run's folder, a newer run's
                // record) keeps whichever knows its Live Photo video.
                .GroupBy(p => p.StagedPath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.FirstOrDefault(p => p.StagedVideoPath is not null) ?? g.First())
                .Where(p => IsOwn(p.StagedPath))
                .Select(p => p.StagedVideoPath is { } video && !IsOwn(video)
                    ? p with { StagedVideoPath = null, OriginalVideoPath = null }
                    : p)
                .Select(p => p.WithSizeRead())
                .ToList(),
            unreachable.ToList());
    }

    private static Dictionary<string, long> NewestRunByStagedPath(IEnumerable<(CleanupRun Run, List<RunFile> Files)> stageRuns)
    {
        var newestRunOf = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (run, files) in stageRuns.OrderBy(r => r.Run.RanAt).ThenBy(r => r.Run.Id))
        {
            foreach (var file in files)
            {
                if (file.Destination is { } staged)
                    newestRunOf[staged] = run.Id;
                if (file.VideoDestination is { } stagedVideo)
                    newestRunOf[stagedVideo] = run.Id;
            }
        }
        return newestRunOf;
    }

    // A photo's outcomes are put together across runs, oldest first: a retry of a
    // Live Photo whose video was staged first records only the photo's move. A photo
    // that came back and was staged again can be in two places; each one still
    // there is listed, the newest with the newest video, any other video on its own.
    private static List<StagedPhoto> FromRecordedPhotos(
        List<(CleanupRun Run, List<RunFile> Files)> runs, Func<string, bool> isOwn, HashSet<string> unreachable)
    {
        var byOriginal = new Dictionary<string, RecordedPhoto>(StringComparer.OrdinalIgnoreCase);
        foreach (var (run, files) in runs.OrderBy(r => r.Run.RanAt).ThenBy(r => r.Run.Id))
        {
            foreach (var file in files)
            {
                if (!byOriginal.TryGetValue(file.Path, out var photo))
                    byOriginal[file.Path] = photo = new RecordedPhoto(run.Target);
                photo.VideoPath ??= file.VideoPath;
                if (file.Destination is { } staged)
                    photo.Stills.Add((staged, run.Destination));
                if (file.VideoDestination is { } stagedVideo)
                    photo.Videos.Add((stagedVideo, run.Destination));
            }
        }

        bool IsStillThere((string Path, string? StageFolder) staged)
        {
            if (!isOwn(staged.Path))
                return false;
            var check = Check(staged.Path);
            if (check == PathCheck.Unknown)
                unreachable.Add(staged.StageFolder ?? Path.GetDirectoryName(staged.Path)!);
            return check == PathCheck.Present;
        }

        var photos = new List<StagedPhoto>();
        foreach (var (original, photo) in byOriginal)
        {
            var stills = photo.Stills.DistinctBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                .Where(IsStillThere).Select(s => s.Path).ToList();
            var videos = photo.VideoPath is null ? []
                : photo.Videos.DistinctBy(v => v.Path, StringComparer.OrdinalIgnoreCase)
                    .Where(IsStillThere).Select(v => v.Path).ToList();
            var newestVideo = videos.LastOrDefault();
            for (int i = 0; i < stills.Count; i++)
            {
                bool withVideo = i == stills.Count - 1 && newestVideo is not null;
                photos.Add(new StagedPhoto(photo.Target, stills[i], original,
                    withVideo ? newestVideo : null, withVideo ? photo.VideoPath : null));
            }
            // A video with no still left to go with (the photo failed to stage, or
            // was moved away by hand) goes back or is deleted on its own.
            foreach (var video in stills.Count > 0 ? videos.SkipLast(1) : videos)
                photos.Add(new StagedPhoto(photo.Target, video, photo.VideoPath!, null, null) { OriginalStillPath = original });
        }
        return photos;
    }

    private sealed class RecordedPhoto(TargetSettings target)
    {
        public TargetSettings Target { get; } = target;
        public string? VideoPath { get; set; }
        public List<(string Path, string? StageFolder)> Stills { get; } = [];
        public List<(string Path, string? StageFolder)> Videos { get; } = [];
    }

    // Synology's own folders (@eaDir thumbnails, #recycle, #snapshot) and hidden
    // ones hold no photos a run staged.
    private static List<StagedPhoto> FromStageFolder(TargetSettings target, string stageFolder)
    {
        // Skipped folders aren't even opened: one that denies access mustn't hide the
        // readable photos around it.
        var files = new List<string>();
        var folders = new Stack<string>([stageFolder]);
        while (folders.TryPop(out var folder))
        {
            files.AddRange(Directory.EnumerateFiles(folder).Where(PhotoScanner.IsPhotoOrVideo));
            foreach (var subfolder in Directory.EnumerateDirectories(folder))
                if (Path.GetFileName(subfolder) is not (['@' or '#' or '$' or '.', ..]))
                    folders.Push(subfolder);
        }
        var videoOfStill = PhotoScanner.LivePhotoVideoByStill(files);
        var pairedVideos = videoOfStill.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string OriginalOf(string staged) => Path.Combine(target.FolderPath, Path.GetRelativePath(stageFolder, staged));
        return files
            .Where(f => !pairedVideos.Contains(f))
            .Select(f => videoOfStill.TryGetValue(f, out var video)
                ? new StagedPhoto(target, f, OriginalOf(f), video, OriginalOf(video))
                : new StagedPhoto(target, f, OriginalOf(f), null, null))
            .ToList();
    }

    private enum PathCheck
    {
        Present,
        Missing,
        Unknown,
    }

    // File.Exists and Directory.Exists answer false for a path that can't be read
    // as well as for one that's gone; only "not found" means gone.
    private static PathCheck Check(string path)
    {
        try
        {
            File.GetAttributes(path);
            return PathCheck.Present;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return PathCheck.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return PathCheck.Unknown;
        }
    }
}
