using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using MetadataExtractor.Formats.QuickTime;

namespace PhotoRecull.Core;

public record ScanProgress(int FilesChecked, int FilesTotal, string CurrentFile);

public record PhotoFile(
    string Path,
    string FileName,
    DateTimeOffset? Timestamp,
    bool IsIphone,
    string? TimestampSource   // "filename", "exif", "video", or null if unknown
)
{
    // Includes the Live Photo video, when there is one.
    public long SizeBytes { get; init; }

    // A Live Photo's motion part (IMG_1234.MOV next to IMG_1234.HEIC). Google
    // Photos shows the pair as one item, so it's kept or removed with its photo.
    public string? LivePhotoVideoPath { get; init; }
}

/// <summary>
/// Scans a local folder for photos, identifies iPhone-origin files, and
/// extracts their capture timestamps.
/// </summary>
public static class PhotoScanner
{
    // Matches OneDrive iOS camera upload format: 20260411_073333208_iOS.heic
    // Groups: year, month, day, hour, min, sec, ms, extension
    private static readonly Regex IosCameraRegex = new(
        @"^(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})(\d{3})_iOS\.(\w+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> StillExtensions = [".heic", ".jpg", ".jpeg", ".png"];

    private static readonly HashSet<string> VideoExtensions = [".mp4", ".mov"];

    public static bool IsPhotoOrVideo(string path)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return StillExtensions.Contains(extension) || VideoExtensions.Contains(extension);
    }

    public static List<PhotoFile> ScanFolder(
        string folderPath, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        // Listing a large or network folder can take a while on its own, so a
        // cancel is honored between entries rather than only once reading starts.
        var photoFiles = new List<string>();
        foreach (var file in System.IO.Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsPhotoOrVideo(file))
                photoFiles.Add(file);
        }
        return ReadPhotos(photoFiles, progress, cancellationToken);
    }

    public static List<PhotoFile> ReadPhotos(
        IReadOnlyList<string> photoFiles, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var liveVideoByStill = LivePhotoVideoByStill(photoFiles, cancellationToken);
        var liveVideos = liveVideoByStill.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filesToRead = photoFiles.Where(f => !liveVideos.Contains(f)).ToList();

        var results = new List<PhotoFile>();
        for (int i = 0; i < filesToRead.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = filesToRead[i];

            progress?.Report(new ScanProgress(FilesChecked: i + 1, FilesTotal: filesToRead.Count, CurrentFile: file));
            long sizeBytes;
            try
            {
                sizeBytes = new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // OneDrive (or the user) removed or renamed it since the folder was
                // listed; it's no longer there to keep or remove, so skip it.
                continue;
            }
            var liveVideo = liveVideoByStill.GetValueOrDefault(file);
            if (liveVideo is not null)
                sizeBytes += SizeOrZeroIfGone(liveVideo);
            var fileName = System.IO.Path.GetFileName(file);
            var (isIphone, timestamp, source) = ClassifyFile(file, fileName);
            results.Add(new PhotoFile(file, fileName, timestamp, isIphone, source)
            {
                SizeBytes = sizeBytes,
                LivePhotoVideoPath = liveVideo,
            });
        }

        return results;
    }

    // iPhone gives a Live Photo's still and video the same name, and stamps both
    // with the same content identifier. A name alone isn't enough: a video
    // recorded separately can share a still's name, and must be kept or removed
    // on its own.
    private static Dictionary<string, string> LivePhotoVideoByStill(
        IReadOnlyList<string> photoFiles, CancellationToken cancellationToken)
    {
        var liveVideoByStill = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sameNamedPairs = photoFiles
            .GroupBy(f => System.IO.Path.ChangeExtension(f, null), StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                Stills: g.Where(f => StillExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToList(),
                Videos: g.Where(f => System.IO.Path.GetExtension(f).Equals(".mov", StringComparison.OrdinalIgnoreCase)).ToList()))
            .Where(pair => pair.Stills.Count == 1 && pair.Videos.Count == 1);
        foreach (var (stills, videos) in sameNamedPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LivePhotoContentIdentifier(stills[0]) is { } identifier && identifier == LivePhotoContentIdentifier(videos[0]))
                liveVideoByStill[stills[0]] = videos[0];
        }
        return liveVideoByStill;
    }

    private static string? LivePhotoContentIdentifier(string path)
    {
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(path);
            var identifier = dirs.OfType<AppleMakernoteDirectory>().FirstOrDefault()?.GetString(AppleMakernoteDirectory.TagContentIdentifier)
                ?? dirs.OfType<QuickTimeMetadataHeaderDirectory>().FirstOrDefault()?.GetString(QuickTimeMetadataHeaderDirectory.TagContentIdentifier);
            return string.IsNullOrEmpty(identifier) ? null : identifier;
        }
        catch
        {
            return null;
        }
    }

    private static long SizeOrZeroIfGone(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return 0;
        }
    }

    private static (bool IsIphone, DateTimeOffset? Timestamp, string? Source) ClassifyFile(
        string path, string fileName)
    {
        // Fast path: filename matches the iOS upload pattern → definitely iPhone, parse timestamp from name
        var match = IosCameraRegex.Match(fileName);
        if (match.Success)
        {
            var ts = ParseFilenameTimestamp(match);
            return (true, ts, "filename");
        }

        // Fallback: read EXIF (or a video's QuickTime metadata) to check Make/Model and get timestamp
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(path);
            if (dirs.OfType<QuickTimeMetadataHeaderDirectory>().FirstOrDefault() is { } quickTime)
                return ClassifyVideo(quickTime);

            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            bool isIphone = IsIphone(ifd0?.GetString(ExifDirectoryBase.TagMake), ifd0?.GetString(ExifDirectoryBase.TagModel));
            DateTimeOffset? timestamp = isIphone && subIfd is not null
                ? ExifCaptureTime(
                    subIfd.GetString(ExifDirectoryBase.TagDateTimeOriginal),
                    subIfd.GetString(ExifDirectoryBase.TagSubsecondTimeOriginal),
                    subIfd.GetString(ExifDirectoryBase.TagTimeZoneOriginal))
                : null;
            string? source = timestamp.HasValue ? "exif" : null;

            return (isIphone, timestamp, source);
        }
        catch
        {
            return (false, null, null);
        }
    }

    // Apple's creation date is when recording started, which is what Google Photos
    // shows; the reader returns it in local time. The movie header's date isn't
    // a fallback: it's when the file was written, so a long video's end, which
    // would match nothing and make a kept video look deleted. Without a start
    // time the video is skipped and left alone.
    private static (bool IsIphone, DateTimeOffset? Timestamp, string? Source) ClassifyVideo(
        QuickTimeMetadataHeaderDirectory quickTime)
    {
        bool isIphone = IsIphone(
            quickTime.GetString(QuickTimeMetadataHeaderDirectory.TagMake),
            quickTime.GetString(QuickTimeMetadataHeaderDirectory.TagModel));
        DateTimeOffset? timestamp = isIphone && quickTime.TryGetDateTime(QuickTimeMetadataHeaderDirectory.TagCreationDate, out var started)
            ? new DateTimeOffset(started).ToUniversalTime()
            : null;
        return (isIphone, timestamp, timestamp.HasValue ? "video" : null);
    }

    /// <summary>
    /// The OneDrive filename encodes UTC time: 20260411_073333208_iOS.heic
    /// → 2026-04-11 07:33:33.208 UTC
    /// </summary>
    private static DateTimeOffset ParseFilenameTimestamp(Match m)
    {
        int year  = int.Parse(m.Groups[1].Value);
        int month = int.Parse(m.Groups[2].Value);
        int day   = int.Parse(m.Groups[3].Value);
        int hour  = int.Parse(m.Groups[4].Value);
        int min   = int.Parse(m.Groups[5].Value);
        int sec   = int.Parse(m.Groups[6].Value);
        int ms    = int.Parse(m.Groups[7].Value);

        return new DateTimeOffset(year, month, day, hour, min, sec, ms, TimeSpan.Zero);
    }

    private static bool IsIphone(string? make, string? model) =>
        string.Equals(make, "Apple", StringComparison.OrdinalIgnoreCase)
        && (model ?? "").Contains("iPhone", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Combines DateTimeOriginal ("2026:04:11 10:33:33"), SubSecTimeOriginal
    /// ("208") and OffsetTimeOriginal ("+03:00" or "-04:00") into a UTC time.
    /// </summary>
    public static DateTimeOffset? ExifCaptureTime(string? dateTimeOriginal, string? subSecond, string? offset)
    {
        if (!DateTime.TryParseExact(dateTimeOriginal, "yyyy:MM:dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return null;

        // Digits of a second's fraction: "2" is 200 ms, "208" is 208 ms.
        if (subSecond is { Length: > 0 } && subSecond.All(char.IsAsciiDigit))
            dt = dt.AddMilliseconds(int.Parse(subSecond.PadRight(3, '0')[..3], CultureInfo.InvariantCulture));

        TimeSpan utcOffset = TimeSpan.Zero;
        if (offset is not null && TimeSpan.TryParseExact(
            offset.TrimStart('+', '-'), @"hh\:mm", CultureInfo.InvariantCulture, out var magnitude))
        {
            utcOffset = offset.StartsWith('-') ? -magnitude : magnitude;
        }

        return new DateTimeOffset(dt, utcOffset).ToUniversalTime();
    }
}
