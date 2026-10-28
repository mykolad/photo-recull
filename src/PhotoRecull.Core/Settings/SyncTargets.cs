namespace PhotoRecull.Core.Settings;

public static class SyncTargets
{
    public static ISyncTarget Create(TargetSettings settings) => settings.Kind switch
    {
        TargetKind.OneDrive => new OneDriveTarget(settings.FolderPath),
        TargetKind.SynologyPhotos => throw new NotSupportedException(
            "Synology Photos isn't supported yet (https://github.com/mykolad/photo-recull/issues/1)."),
        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Kind, null),
    };

    public static string DisplayName(TargetKind kind) => kind switch
    {
        TargetKind.OneDrive => "OneDrive",
        TargetKind.SynologyPhotos => "Synology Photos",
        _ => kind.ToString(),
    };
}
