namespace PhotoRecull.Core;

/// <summary>
/// A backup of the user's iPhone photos, other than Google Photos, where
/// PhotoRecull repeats the culls made in Google Photos.
/// </summary>
public interface ISyncTarget
{
    string DisplayName { get; }

    string FolderPath { get; }

    List<PhotoFile> Scan(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}
