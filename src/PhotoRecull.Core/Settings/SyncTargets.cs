namespace PhotoRecull.Core.Settings;

public static class SyncTargets
{
    public static ISyncTarget Create(TargetSettings settings) => new FolderTarget(settings.Name, settings.FolderPath);

    // The folders the OneDrive app syncs for the signed-in Windows user: a personal
    // account, a work or school one, or both.
    private static readonly string[] OneDriveFolderVariables = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    public static bool IsInOneDrive(string folderPath) =>
        OneDriveFolderVariables
            .Select(Environment.GetEnvironmentVariable)
            .Any(root => !string.IsNullOrEmpty(root) && FileActions.IsSameOrInside(folderPath, root));

    // What a new target is called until the user names it: the service that
    // backs up to the folder where that can be told, otherwise the NAS or the
    // folder itself. Never a name another target already has.
    public static string SuggestedName(string folderPath, IReadOnlyCollection<string> namesInUse)
    {
        var name = NameFromFolder(folderPath.Trim());
        var unique = name;
        for (var number = 2; namesInUse.Contains(unique, StringComparer.OrdinalIgnoreCase); number++)
            unique = $"{name} {number}";
        return unique;
    }

    private static string NameFromFolder(string folderPath)
    {
        if (folderPath.Length == 0)
            return "Folder";
        if (IsInOneDrive(folderPath))
            return "OneDrive";
        var folders = Path.TrimEndingDirectorySeparator(folderPath).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        // Synology Photos' mobile backup always lands in a MobileBackup folder.
        if (folders.Contains("MobileBackup", StringComparer.OrdinalIgnoreCase))
            return "Synology Photos";
        if (FileActions.IsNetworkFolder(folderPath) && new Uri(FileActions.ComparablePath(folderPath)).Host is { Length: > 0 } server)
            return server;
        return folders.Length > 1 ? folders[^1] : $"Drive {folderPath[0]}";
    }
}
