using System.Text.Json;

namespace PhotoRecull.Core;

/// <summary>
/// The picked Google Photos timestamps plus the context they were captured in,
/// persisted so the review/stage/delete workflow can be repeated without
/// re-opening the picker each time.
/// </summary>
public record PickCacheData(
    DateTimeOffset CapturedAtUtc,
    DateOnly? From,
    DateOnly? To,
    List<DateTimeOffset> Timestamps);

/// <summary>Reads and writes <see cref="PickCacheData"/> as JSON.</summary>
public static class PickCache
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // Written to a temporary file next to it and then swapped in, so an
    // interrupted save (e.g. while picking again) leaves the previous pick intact.
    public static void Save(string path, PickCacheData data)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, Options));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static PickCacheData Load(string path) =>
        JsonSerializer.Deserialize<PickCacheData>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"Cache file is empty or invalid: {path}");
}
