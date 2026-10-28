namespace PhotoRecull.Core;

public sealed class OneDriveTarget(string cameraRollFolderPath) : ISyncTarget
{
    public string DisplayName => "OneDrive";

    public string FolderPath { get; } = cameraRollFolderPath;

    public List<PhotoFile> Scan(IProgress<string>? fileBeingRead = null, CancellationToken cancellationToken = default) =>
        PhotoScanner.ScanFolder(FolderPath, fileBeingRead, cancellationToken);
}
