using PhotoRecull.Core;

namespace PhotoRecull.App.Services;

internal static class PickStore
{
    public static void Save(Guid cleanupId, PickCacheData pick)
    {
        Directory.CreateDirectory(AppDataPaths.PicksFolder);
        PickCache.Save(PathFor(cleanupId), pick);
    }

    // An unreadable or damaged pick is treated as no pick, so the cleanup still
    // opens and the user can simply pick again.
    public static PickCacheData? LoadOrNull(Guid cleanupId)
    {
        try
        {
            return PickCache.Load(PathFor(cleanupId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or System.Text.Json.JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static string PathFor(Guid cleanupId) => Path.Combine(AppDataPaths.PicksFolder, $"{cleanupId}.json");
}
