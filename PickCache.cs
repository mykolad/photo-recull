using System.Text.Json;

namespace PhotoRecull;

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

    public static void Save(string path, PickCacheData data) =>
        File.WriteAllText(path, JsonSerializer.Serialize(data, Options));

    public static PickCacheData Load(string path) =>
        JsonSerializer.Deserialize<PickCacheData>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"Cache file is empty or invalid: {path}");
}
