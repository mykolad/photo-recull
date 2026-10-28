using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class FileActionsTests : IDisposable
{
    private readonly string _targetFolder = Directory.CreateTempSubdirectory("photorecull-target-").FullName;
    private readonly string _stageFolder;

    public FileActionsTests() => _stageFolder = Path.Combine(_targetFolder, "_staged_test");

    public void Dispose() => Directory.Delete(_targetFolder, recursive: true);

    private PhotoFile CreatePhoto(string fileName)
    {
        var path = Path.Combine(_targetFolder, fileName);
        File.WriteAllText(path, fileName);
        return new PhotoFile(path, fileName, DateTimeOffset.UtcNow, IsIphone: true, TimestampSource: "filename");
    }

    [Fact]
    public void MoveToStageFolder_MovesEveryPhotoAndCreatesTheFolder()
    {
        var photos = new[] { CreatePhoto("a.heic"), CreatePhoto("b.heic") };

        var result = FileActions.MoveToStageFolder(photos, _stageFolder);

        Assert.Equal(2, result.SucceededCount);
        Assert.Empty(result.Failures);
        Assert.All(photos, p => Assert.False(File.Exists(p.Path)));
        Assert.True(File.Exists(Path.Combine(_stageFolder, "a.heic")));
        Assert.True(File.Exists(Path.Combine(_stageFolder, "b.heic")));
    }

    [Fact]
    public void MoveToStageFolder_NeverOverwritesAndContinuesAfterAFailure()
    {
        Directory.CreateDirectory(_stageFolder);
        File.WriteAllText(Path.Combine(_stageFolder, "a.heic"), "already staged");
        var photos = new[] { CreatePhoto("a.heic"), CreatePhoto("b.heic") };

        var result = FileActions.MoveToStageFolder(photos, _stageFolder);

        Assert.Equal(1, result.SucceededCount);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("a.heic", failure.Photo.FileName);
        Assert.Equal("already staged", File.ReadAllText(Path.Combine(_stageFolder, "a.heic")));
        Assert.True(File.Exists(photos[0].Path));
    }

    [Fact]
    public void DeletePermanently_DeletesEveryPhoto()
    {
        var photos = new[] { CreatePhoto("a.heic"), CreatePhoto("b.heic") };

        var result = FileActions.DeletePermanently(photos);

        Assert.Equal(2, result.SucceededCount);
        Assert.All(photos, p => Assert.False(File.Exists(p.Path)));
    }

    [Fact]
    public void DeletePermanently_ReportsLockedFileAndDeletesTheRest()
    {
        var locked = CreatePhoto("locked.heic");
        var unlocked = CreatePhoto("unlocked.heic");

        FileActionResult result;
        using (File.Open(locked.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            result = FileActions.DeletePermanently([locked, unlocked]);

        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(locked, Assert.Single(result.Failures).Photo);
        Assert.False(File.Exists(unlocked.Path));
    }
}
