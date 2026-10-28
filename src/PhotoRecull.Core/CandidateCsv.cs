using System.Globalization;

namespace PhotoRecull.Core;

public static class CandidateCsv
{
    public static void WriteNewestFirst(string path, IEnumerable<PhotoFile> photos)
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine("TimestampUTC,TimestampSource,FileName,FullPath");
        foreach (var p in photos.OrderByDescending(x => x.Timestamp))
        {
            var ts = p.Timestamp.HasValue
                ? p.Timestamp.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : "";
            sw.WriteLine($"{ts},{p.TimestampSource ?? ""},{EscapeCsvField(p.FileName)},{EscapeCsvField(p.Path)}");
        }
    }

    private static string EscapeCsvField(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
