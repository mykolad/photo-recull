using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoRecull.Core;

public sealed class JsonFileStore<T>(string filePath) where T : new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = filePath;

    // A damaged file is moved aside rather than thrown on, so the app can still
    // start; the original stays next to it for recovery.
    public T LoadOrCreateDefault()
    {
        if (!File.Exists(FilePath))
            return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath), Options) ?? new T();
        }
        catch (JsonException)
        {
            File.Move(FilePath, $"{FilePath}.damaged-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            return new T();
        }
    }

    // Written to a temporary file first and then swapped in, so a crash or power
    // loss mid-write can't leave the user's library or settings half-written.
    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, Options));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }
}
