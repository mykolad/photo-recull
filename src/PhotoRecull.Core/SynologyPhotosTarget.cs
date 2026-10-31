using System.Globalization;

namespace PhotoRecull.Core;

// Synology Photos reached as a folder on the network (\\nas\home\Photos\MobileBackup\iPhone)
// or a mapped drive. Its mobile backup files photos in date folders (2026\01,
// or 2026-01), so a cleanup only reads the folders its days can be in.
public sealed class SynologyPhotosTarget(string folderPath) : ISyncTarget
{
    public string DisplayName => "Synology Photos";

    public string FolderPath { get; } = folderPath;

    public List<PhotoFile> Scan(
        DateOnly? firstDay, DateOnly? lastDay,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(FolderPath))
            throw new DirectoryNotFoundException(
                $"Can't open {FolderPath}. Check that the NAS is on and that File Explorer can open this folder.");
        var photoFiles = ListPhotoFiles(FolderPath, firstDay, lastDay, cancellationToken);
        return PhotoScanner.ReadPhotos(photoFiles, progress, cancellationToken);
    }

    // Outside the Photos folder that Synology Photos indexes: staged photos left
    // inside it would show up in Synology Photos again.
    public string DefaultStageFolder(DateOnly firstDay, DateOnly lastDay)
    {
        var folder = Path.TrimEndingDirectorySeparator(FolderPath);
        var photosFolder = FolderAndItsParents(folder)
            .FirstOrDefault(f => Path.GetFileName(f).Equals("Photos", StringComparison.OrdinalIgnoreCase));
        var outsideIndexedFolder = Path.GetDirectoryName(photosFolder ?? folder) ?? folder;
        return Path.Combine(outsideIndexedFolder, "PhotoRecull staged", $"{firstDay:yyyy-MM-dd}_{lastDay:yyyy-MM-dd}");
    }

    // Deleting over the network skips the Windows Recycle Bin; DSM catches the
    // files here instead when the Recycle Bin is on for the shared folder.
    public string RecycleBinFolder =>
        Path.Combine(Path.GetPathRoot(Path.GetFullPath(FolderPath)) ?? FolderPath, "#recycle");

    public bool HasRecycleBin => Directory.Exists(RecycleBinFolder);

    // A folder is named after the capture date where the photo was taken, while
    // the days are in the user's time zone; a day's margin covers the difference.
    // A folder skipped by mistake is only a photo left alone, never one removed.
    public static List<string> ListPhotoFiles(
        string rootFolder, DateOnly? firstDay, DateOnly? lastDay, CancellationToken cancellationToken = default)
    {
        var firstDayWithMargin = firstDay?.AddDays(-1);
        var lastDayWithMargin = lastDay?.AddDays(1);
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
            foreach (var subfolder in Directory.EnumerateDirectories(folder.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(subfolder);
                if (IsSynologyOrSystemFolder(name))
                    continue;
                var dates = DateFolder.FromName(name, folder.Dates) ?? folder.Dates;
                if (dates is null || dates.Overlaps(firstDayWithMargin, lastDayWithMargin))
                    foldersToList.Push((subfolder, dates));
            }
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    // @eaDir holds Synology's thumbnails, #recycle and #snapshot its Recycle Bin
    // and snapshots.
    private static bool IsSynologyOrSystemFolder(string name) => name.Length > 0 && name[0] is '@' or '#' or '.' or '$';

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
