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
/// as kept. That only ever keeps a copy, never removes one. For the same reason, anything
/// that could leave a photo out stops the read instead: a .json that can't be read, or a
/// sidecar whose capture time can't be.
/// </summary>
public static class GoogleTakeout
{
    /// <summary>Reads every .zip file and folder given; a folder is read with its subfolders.</summary>
    public static TakeoutRead ReadCaptureTimes(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var timestamps = new List<DateTimeOffset>();
        int sidecars = 0, trashed = 0;

        void Add(Stream json, string name)
        {
            switch (ReadSidecar(json, name))
            {
                case Sidecar { IsTrashed: true }:
                    sidecars++;
                    trashed++;
                    break;
                case Sidecar sidecar:
                    sidecars++;
                    // An album's folder can hold its own copy of a photo and its sidecar:
                    // the same item, with the same Google Photos link, counts once. Without
                    // a link every sidecar counts, which can only keep more.
                    if (sidecar.Url is null || seen.Add(sidecar.Url))
                        timestamps.Add(sidecar.TakenAt);
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
                    Add(stream, file);
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
                    Add(stream, $"{path} › {entry.FullName}");
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

    private sealed record Sidecar(DateTimeOffset TakenAt, string? Url, bool IsTrashed);

    // Null for JSON that isn't a photo's sidecar: one with no photoTakenTime, such as an
    // album's metadata.json or Takeout's own files. JSON that can't be read, or a
    // photoTakenTime that can't, throws: that photo would otherwise be left out, and its
    // copies in the backups would look deleted.
    private static Sidecar? ReadSidecar(Stream json, string name)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Can't read {name}: it isn't valid JSON. Export again, or remove it if it isn't Google Photos'.", ex);
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("photoTakenTime", out var taken))
                return null;
            if (taken.ValueKind != JsonValueKind.Object
                || !taken.TryGetProperty("timestamp", out var timestamp)
                || !TryReadSeconds(timestamp, out long seconds))
                throw new InvalidDataException($"Can't read the capture time in {name}.");
            // Zero is Takeout's "no capture time": such an item has nothing to match by.
            if (seconds == 0)
                return null;
            string? url = root.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String && u.GetString() is { Length: > 0 } link ? link : null;
            bool isTrashed = root.TryGetProperty("trashed", out var trashed) && trashed.ValueKind == JsonValueKind.True;
            return new Sidecar(DateTimeOffset.FromUnixTimeSeconds(seconds), url, isTrashed);
        }
    }

    // Takeout writes the seconds as a string ("1695123456"); a number is read too. Before
    // 1970 they're negative. Out of DateTimeOffset's range counts as unreadable.
    private static bool TryReadSeconds(JsonElement value, out long seconds)
    {
        seconds = 0;
        bool read = value.ValueKind switch
        {
            JsonValueKind.String => long.TryParse(value.GetString(), System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out seconds),
            JsonValueKind.Number => value.TryGetInt64(out seconds),
            _ => false,
        };
        return read
            && seconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds()
            && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds();
    }
}
