using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace PhotoRecull.Core;

public record PhotoFile(
    string Path,
    string FileName,
    DateTimeOffset? Timestamp,
    bool IsIphone,
    string? TimestampSource   // "filename", "exif", or null if unknown
);

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

    private static readonly HashSet<string> SupportedExtensions =
        [".heic", ".jpg", ".jpeg", ".png", ".mp4", ".mov"];

    public static List<PhotoFile> ScanFolder(
        string folderPath, IProgress<string>? fileBeingRead = null, CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        var results = new List<PhotoFile>();

        foreach (var file in System.IO.Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (!SupportedExtensions.Contains(ext))
                continue;

            fileBeingRead?.Report(file);
            var fileName = Path.GetFileName(file);
            var (isIphone, timestamp, source) = ClassifyFile(file, fileName);
            results.Add(new PhotoFile(file, fileName, timestamp, isIphone, source));
        }

        return results;
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

        // Fallback: read EXIF to check Make/Model and get timestamp
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(path);
            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            bool isIphone = IsIphoneExif(ifd0);
            DateTimeOffset? timestamp = isIphone ? ReadExifTimestamp(subIfd) : null;
            string? source = timestamp.HasValue ? "exif" : null;

            return (isIphone, timestamp, source);
        }
        catch
        {
            return (false, null, null);
        }
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

    private static bool IsIphoneExif(ExifIfd0Directory? ifd0)
    {
        if (ifd0 is null) return false;
        var make  = ifd0.GetString(ExifDirectoryBase.TagMake)  ?? "";
        var model = ifd0.GetString(ExifDirectoryBase.TagModel) ?? "";
        return make.Equals("Apple", StringComparison.OrdinalIgnoreCase)
            && model.Contains("iPhone", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads DateTimeOriginal + SubSecTimeOriginal + TimeZoneOriginal from EXIF
    /// and returns a UTC DateTimeOffset.
    /// </summary>
    private static DateTimeOffset? ReadExifTimestamp(ExifSubIfdDirectory? sub)
    {
        if (sub is null) return null;

        // DateTimeOriginal: "2026:04:11 10:33:33"
        var raw = sub.GetString(ExifDirectoryBase.TagDateTimeOriginal);
        if (raw is null) return null;

        if (!DateTime.TryParseExact(raw, "yyyy:MM:dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return null;

        // Sub-second: "208" → 208 ms
        int ms = 0;
        var subSec = sub.GetString(ExifDirectoryBase.TagSubsecondTimeOriginal);
        if (subSec is not null && int.TryParse(subSec, out int parsed))
            ms = parsed;

        dt = dt.AddMilliseconds(ms);

        // Timezone offset: "+03:00"
        TimeSpan offset = TimeSpan.Zero;
        var tzRaw = sub.GetString(ExifDirectoryBase.TagTimeZoneOriginal);
        if (tzRaw is not null && TimeSpan.TryParseExact(
            tzRaw.TrimStart('+'), @"hh\:mm", CultureInfo.InvariantCulture, out var tz))
        {
            offset = tzRaw.StartsWith('-') ? -tz : tz;
        }

        return new DateTimeOffset(dt, offset).ToUniversalTime();
    }
}
