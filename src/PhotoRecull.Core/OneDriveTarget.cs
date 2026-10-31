namespace PhotoRecull.Core;

public sealed class OneDriveTarget(string cameraRollFolderPath) : ISyncTarget
{
    public string DisplayName => "OneDrive";

    public string FolderPath { get; } = cameraRollFolderPath;

    public List<PhotoFile> Scan(
        DateOnly? firstDay, DateOnly? lastDay,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        PhotoScanner.ScanFolder(FolderPath, progress, cancellationToken);

    // Next to the Camera Roll rather than in it, so a later scan doesn't find them.
    public string? DefaultStageFolder(DateOnly firstDay, DateOnly lastDay) =>
        Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(FolderPath)) ?? FolderPath,
            $"_staged_{firstDay:yyyy-MM-dd}_{lastDay:yyyy-MM-dd}");
}
