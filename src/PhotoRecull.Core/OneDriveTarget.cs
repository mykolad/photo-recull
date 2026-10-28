namespace PhotoRecull.Core;

public sealed class OneDriveTarget(string cameraRollFolderPath) : ISyncTarget
{
    public string DisplayName => "OneDrive";

    public string FolderPath { get; } = cameraRollFolderPath;

    public List<PhotoFile> Scan(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        PhotoScanner.ScanFolder(FolderPath, progress, cancellationToken);
}
