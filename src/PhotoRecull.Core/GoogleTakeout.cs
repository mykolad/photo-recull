using System.IO.Compression;
using System.Text.Json;

namespace PhotoRecull.Core;

/// <summary>What a Google Takeout export says Google Photos holds.</summary>
public record TakeoutRead(List<DateTimeOffset> Timestamps, int SidecarsRead, int Trashed);

/// <summary>
/// Reads the capture times of every photo in a Google Takeout export of Google Photos, in
/// place of picking the photos you kept in the Photos Picker. Takeout writes a JSON sidecar
/// next to each photo and video; its <c>photoTakenTime</c> is the capture time in UTC, to the
/// second, as the Picker gives it. An export is a set of .zip files (read without
/// extracting) or the folder they were extracted to.
///
/// Takeout is a snapshot: a photo deleted from Google Photos after the export still counts
/// as kept. That only ever keeps a copy, never removes one.
/// </summary>
public static class GoogleTakeout
{
    /// <summary>Reads every .zip file and folder given; a folder is read with its subfolders.</summary>
    public static TakeoutRead ReadCaptureTimes(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<(string Title, long Seconds)>();
        var timestamps = new List<DateTimeOffset>();
        int sidecars = 0, trashed = 0;

        void Add(Stream json)
        {
            switch (ReadSidecar(json))
            {
                case Sidecar { IsTrashed: true }:
                    sidecars++;
                    trashed++;
                    break;
                case Sidecar sidecar:
                    sidecars++;
                    // An album's folder holds its own copy of each photo and sidecar: the
                    // same photo, with the same title and time, counts once. Two burst
                    // frames kept in the same second have different titles.
                    if (seen.Add((sidecar.Title, sidecar.Seconds)))
                        timestamps.Add(DateTimeOffset.FromUnixTimeSeconds(sidecar.Seconds));
                    break;
            }
        }

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = File.OpenRead(file);
                    Add(stream);
                }
            }
            else if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        continue;
                    using var stream = entry.Open();
                    Add(stream);
                }
            }
            else
            {
                throw new FileNotFoundException(
                    $"Not a Google Takeout folder or .zip file: {path}. Extract a .tgz export first, or choose .zip when you create it.", path);
            }
        }

        return new TakeoutRead(timestamps, sidecars, trashed);
    }

    private sealed record Sidecar(string Title, long Seconds, bool IsTrashed);

    // Null for JSON that isn't a photo's sidecar: an album's metadata.json, Takeout's own
    // files, or one that can't be read.
    private static Sidecar? ReadSidecar(Stream json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("photoTakenTime", out var taken)
                || taken.ValueKind != JsonValueKind.Object
                || !taken.TryGetProperty("timestamp", out var timestamp)
                || !TryReadSeconds(timestamp, out long seconds)
                || seconds <= 0)
                return null;
            string title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            bool isTrashed = root.TryGetProperty("trashed", out var trashed) && trashed.ValueKind == JsonValueKind.True;
            return new Sidecar(title, seconds, isTrashed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Takeout writes the seconds as a string ("1695123456"); a number is read too.
    private static bool TryReadSeconds(JsonElement value, out long seconds)
    {
        seconds = 0;
        return value.ValueKind switch
        {
            JsonValueKind.String => long.TryParse(value.GetString(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out seconds),
            JsonValueKind.Number => value.TryGetInt64(out seconds),
            _ => false,
        };
    }
}
