using PhotoRecull.Core;

namespace PhotoRecull.App.Services;

internal static class PickStore
{
    public static void Save(Guid cleanupId, PickCacheData pick)
    {
        Directory.CreateDirectory(AppDataPaths.PicksFolder);
        PickCache.Save(PathFor(cleanupId), pick);
    }

    public static PickCacheData? LoadOrNull(Guid cleanupId) =>
        File.Exists(PathFor(cleanupId)) ? PickCache.Load(PathFor(cleanupId)) : null;

    private static string PathFor(Guid cleanupId) => Path.Combine(AppDataPaths.PicksFolder, $"{cleanupId}.json");
}
