namespace PhotoRecull.Core.Settings;

public static class SyncTargets
{
    public static ISyncTarget Create(TargetSettings settings) => settings.Kind switch
    {
        TargetKind.OneDrive => new OneDriveTarget(settings.FolderPath),
        TargetKind.SynologyPhotos => new SynologyPhotosTarget(settings.FolderPath),
        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Kind, null),
    };

    public static string DisplayName(TargetKind kind) => kind switch
    {
        TargetKind.OneDrive => "OneDrive",
        TargetKind.SynologyPhotos => "Synology Photos",
        _ => kind.ToString(),
    };
}
