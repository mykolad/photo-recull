using System.Text.RegularExpressions;

namespace PhotoRecull.Core.Settings;

public static class SyncTargets
{
    public static ISyncTarget Create(TargetSettings settings) =>
        new FolderTarget(settings.Name, settings.FolderPath, settings.OnlyIphonePhotos);

    // Targets whose default stage folders would be the same (two folders side by
    // side, or two backups under one Synology Photos folder) each get a folder of
    // their own inside it, named after the target, so photos with the same name
    // in the same place don't collide.
    public static Dictionary<TargetSettings, string?> DefaultStageFolders(
        IEnumerable<TargetSettings> targets, DateOnly firstDay, DateOnly lastDay)
    {
        var folders = targets.Distinct().ToDictionary(t => t, t => Create(t).DefaultStageFolder(firstDay, lastDay));
        var shared = folders
            .Where(f => f.Value is not null)
            .GroupBy(f => FileActions.ComparablePath(f.Value!), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var group in shared)
        {
            var namesInUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (target, folder) in group)
            {
                var name = FolderNameFor(target.Name);
                var unique = name;
                for (var number = 2; !namesInUse.Add(unique); number++)
                    unique = $"{name} {number}";
                folders[target] = Path.Combine(folder!, unique);
            }
        }
        return folders;
    }

    // Characters a folder name can't have become _, and a name Windows keeps for a
    // device (CON, NUL, COM1, also with an extension) gets one in front.
    private static string FolderNameFor(string targetName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat(targetName.Select(c => invalid.Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (name.Length == 0)
            return "Target";
        return ReservedDeviceName.IsMatch(name) ? "_" + name : name;
    }

    private static readonly Regex ReservedDeviceName = new(
        @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(\..*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
