using System.Globalization;

namespace PhotoRecull.Core;

// A folder the user's iPhone photos are backed up to, whichever app puts them
// there: OneDrive's Camera Roll, Synology Photos' mobile backup on a NAS, or any
// other folder File Explorer can open. Subfolders are read too; ones named after
// dates (2026\01, or 2026-01) are only read when the cleanup's days can be in them.
public sealed class FolderTarget(string name, string folderPath) : ISyncTarget
{
    public string DisplayName { get; } = name;

    public string FolderPath { get; } = folderPath;

    public List<PhotoFile> Scan(
        DateOnly? firstDay, DateOnly? lastDay,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(FolderPath))
            throw new DirectoryNotFoundException(FileActions.IsNetworkFolder(FolderPath)
                ? $"Can't open {FolderPath}. Check that the NAS or server is on and that File Explorer can open this folder."
                : $"Can't open {FolderPath}. Check the path, and that its drive is connected.");
        var photoFiles = ListPhotoFiles(FolderPath, firstDay, lastDay, cancellationToken);
        return PhotoScanner.ReadPhotos(photoFiles, progress, cancellationToken);
    }

    // Outside the folder, so its next scan doesn't find the staged photos again,
    // and outside any library app's view of it where that can be told:
    // - A Photos folder (Synology Photos' personal space, \nas\home\Photos) is
    //   shown whole by Synology Photos, so the stage folder goes next to it.
    // - Synology Photos' Shared Space (\nas\photo\MobileBackup) shows the whole
    //   shared folder, which has nothing outside it to tell: the user chooses.
    // - Anything else (OneDrive's Camera Roll) gets one next to the folder.
    public string? DefaultStageFolder(DateOnly firstDay, DateOnly lastDay)
    {
        var folder = Path.TrimEndingDirectorySeparator(FolderPath);
        var folders = FolderAndItsParents(folder).ToList();
        var photosFolder = folders.FirstOrDefault(f => Path.GetFileName(f).Equals("Photos", StringComparison.OrdinalIgnoreCase));
        if (photosFolder is not null)
            return Path.GetDirectoryName(photosFolder) is { } outsidePhotos
                ? Path.Combine(outsidePhotos, StageFolderName, $"{firstDay:yyyy-MM-dd}_{lastDay:yyyy-MM-dd}")
                : null;
        if (folders.Any(f => Path.GetFileName(f).Equals("MobileBackup", StringComparison.OrdinalIgnoreCase)))
            return null;
        return Path.GetDirectoryName(folder) is { } parent
            ? Path.Combine(parent, $"_staged_{firstDay:yyyy-MM-dd}_{lastDay:yyyy-MM-dd}")
            : null;
    }

    // Deleting from a network folder skips the Windows Recycle Bin. A NAS can keep
    // deleted files itself, at the root of the shared folder, when that's turned
    // on: #recycle on Synology (DSM), @Recycle on QNAP. Finding the folder shows it
    // was turned on once, not that it still is.
    public static readonly string[] NasRecycleBinNames = ["#recycle", "@Recycle"];

    public string? NasRecycleBinFolder()
    {
        if (!FileActions.IsNetworkFolder(FolderPath) || Path.GetPathRoot(Path.GetFullPath(FolderPath)) is not { Length: > 0 } shareRoot)
            return null;
        return NasRecycleBinNames.Select(name => Path.Combine(shareRoot, name)).FirstOrDefault(Directory.Exists);
    }

    // A folder is named after the capture date where the photo was taken, while
    // the days are in the user's time zone. Those can be up to 26 hours apart
    // (UTC-12 and UTC+14), so two days' margin covers the difference.
    // A folder skipped by mistake is only a photo left alone, never one removed.
    public static List<string> ListPhotoFiles(
        string rootFolder, DateOnly? firstDay, DateOnly? lastDay, CancellationToken cancellationToken = default)
    {
        var firstDayWithMargin = firstDay?.AddDays(-2);
        var lastDayWithMargin = lastDay?.AddDays(2);
        var files = new List<string>();
        var foldersToList = new Stack<(string Path, DateFolder? Dates)>();
        foldersToList.Push((rootFolder, null));
        while (foldersToList.TryPop(out var folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (PhotoScanner.IsPhotoOrVideo(file))
                    files.Add(file);
            }
            foreach (var subfolder in new DirectoryInfo(folder.Path).EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = subfolder.Name;
                if (IsSkippedFolder(name) || IsLinkToAnotherFolder(subfolder))
                    continue;
                var dates = DateFolder.FromName(name, folder.Dates) ?? folder.Dates;
                if (dates is null || dates.Overlaps(firstDayWithMargin, lastDayWithMargin))
                    foldersToList.Push((subfolder.FullName, dates));
            }
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    // A junction or symbolic link shows another folder's photos a second time: two
    // paths to one file, one matched and one not, would remove the photo that stays.
    // OneDrive's own folders are reparse points too, but not links, so they're read.
    public static bool IsLinkToAnotherFolder(DirectoryInfo folder) =>
        folder.Attributes.HasFlag(FileAttributes.ReparsePoint) && folder.LinkTarget is not null;

    // @eaDir holds Synology's thumbnails, #recycle and #snapshot its Recycle Bin
    // and snapshots. PhotoRecull's own stage folders hold photos waiting for
    // review, which mustn't come up for removal again if one ended up in here.
    private static bool IsSkippedFolder(string name) =>
        name is ['@' or '#' or '.' or '$', ..]
        || name.Equals(StageFolderName, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("_staged_", StringComparison.OrdinalIgnoreCase);

    private const string StageFolderName = "PhotoRecull staged";

    private static IEnumerable<string> FolderAndItsParents(string folder)
    {
        for (string? current = folder; current is not null; current = Path.GetDirectoryName(current))
            yield return current;
    }

    private enum DateFolderSpan
    {
        Year,
        Month,
        Day,
    }

    private sealed record DateFolder(DateOnly First, DateOnly Last, DateFolderSpan Span)
    {
        public bool Overlaps(DateOnly? firstDay, DateOnly? lastDay) =>
            (lastDay is null || First <= lastDay) && (firstDay is null || Last >= firstDay);

        // 2026, then 01 inside it, then 14 inside that; or 2026-01 and 2026-01-14.
        public static DateFolder? FromName(string name, DateFolder? parent)
        {
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                return OfDay(day);
            if (DateOnly.TryParseExact(name, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                return OfMonth(month);
            if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return null;
            return (parent?.Span, name.Length) switch
            {
                (null, 4) when number is >= 1900 and <= 2999 =>
                    new DateFolder(new DateOnly(number, 1, 1), new DateOnly(number, 12, 31), DateFolderSpan.Year),
                (DateFolderSpan.Year, 2) when number is >= 1 and <= 12 =>
                    OfMonth(new DateOnly(parent!.First.Year, number, 1)),
                (DateFolderSpan.Month, 2) when number >= 1 && number <= DateTime.DaysInMonth(parent!.First.Year, parent.First.Month) =>
                    OfDay(new DateOnly(parent.First.Year, parent.First.Month, number)),
                _ => null,
            };
        }

        private static DateFolder OfMonth(DateOnly firstOfMonth) =>
            new(firstOfMonth, firstOfMonth.AddMonths(1).AddDays(-1), DateFolderSpan.Month);

        private static DateFolder OfDay(DateOnly day) => new(day, day, DateFolderSpan.Day);
    }
}
