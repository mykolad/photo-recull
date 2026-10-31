namespace PhotoRecull.Core;

/// <summary>
/// A backup of the user's iPhone photos, other than Google Photos, where
/// PhotoRecull repeats the culls made in Google Photos.
/// </summary>
public interface ISyncTarget
{
    string DisplayName { get; }

    string FolderPath { get; }

    // The days let a target skip folders that can't hold photos from them; it may
    // still return photos from outside, so callers filter by date themselves.
    List<PhotoFile> Scan(
        DateOnly? firstDay, DateOnly? lastDay,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    // Null when no folder outside the photos the target shows can be told; the
    // user then chooses one.
    string? DefaultStageFolder(DateOnly firstDay, DateOnly lastDay);
}
