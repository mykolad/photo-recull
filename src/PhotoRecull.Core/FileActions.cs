namespace PhotoRecull.Core;

public record FileFailure(PhotoFile Photo, string Reason);

public record FileActionResult(int SucceededCount, List<FileFailure> Failures);

public static class FileActions
{
    public static FileActionResult MoveToStageFolder(IEnumerable<PhotoFile> photos, string stageFolder)
    {
        Directory.CreateDirectory(stageFolder);
        return ApplyToEachCollectingFailures(photos,
            p => File.Move(p.Path, Path.Combine(stageFolder, p.FileName), overwrite: false));
    }

    public static FileActionResult DeletePermanently(IEnumerable<PhotoFile> photos) =>
        ApplyToEachCollectingFailures(photos, p => File.Delete(p.Path));

    private static FileActionResult ApplyToEachCollectingFailures(
        IEnumerable<PhotoFile> photos, Action<PhotoFile> action)
    {
        int succeededCount = 0;
        var failures = new List<FileFailure>();
        foreach (var photo in photos)
        {
            try
            {
                action(photo);
                succeededCount++;
            }
            catch (Exception ex)
            {
                failures.Add(new FileFailure(photo, ex.Message));
            }
        }
        return new FileActionResult(succeededCount, failures);
    }
}
