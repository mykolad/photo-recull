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
            MoveDamagedFileAside();
            return new T();
        }
        catch (FileNotFoundException)
        {
            return new T();
        }
    }

    // Another app window starting at the same moment may have moved it aside
    // already (or still be moving it); either way it's no longer in the way.
    private void MoveDamagedFileAside()
    {
        try
        {
            File.Move(FilePath, $"{FilePath}.damaged-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}", overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Written to a temporary file first and then swapped in, so a crash or power
    // loss mid-write can't leave the user's library or settings half-written. Each
    // save gets its own temporary file because two app windows may save at once.
    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, Options));
            MoveOverDestinationRetryingWhileBusy(temporaryPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    // Windows denies replacing a file while another save is replacing it; that
    // only lasts milliseconds, so retry briefly before giving up.
    private void MoveOverDestinationRetryingWhileBusy(string temporaryPath)
    {
        const int maxAttempts = 20;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, FilePath, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < maxAttempts)
            {
                Thread.Sleep(Random.Shared.Next(5, 25));
            }
        }
    }
}
