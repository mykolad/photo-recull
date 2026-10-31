using PhotoRecull.Core;
using PhotoRecull.Core.Library;

namespace PhotoRecull.Tests;

public class FileActionsTests : IDisposable
{
    // The stage folder sits next to the target, as the app's default does.
    private readonly string _root = Directory.CreateTempSubdirectory("photorecull-target-").FullName;
    private readonly string _targetFolder;
    private readonly string _stageFolder;

    public FileActionsTests()
    {
        _targetFolder = Directory.CreateDirectory(Path.Combine(_root, "Camera Roll")).FullName;
        _stageFolder = Path.Combine(_root, "_staged_test");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

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

        var result = FileActions.MoveToStageFolder(photos, _stageFolder, _targetFolder);

        Assert.Equal(2, result.SucceededCount);
        Assert.Empty(result.Failures);
        Assert.All(photos, p => Assert.False(File.Exists(p.Path)));
        Assert.True(File.Exists(Path.Combine(_stageFolder, "a.heic")));
        Assert.True(File.Exists(Path.Combine(_stageFolder, "b.heic")));
    }

    [Fact]
    public void MoveToStageFolder_InsideTheTarget_MovesNothing()
    {
        var photos = new[] { CreatePhoto("a.heic"), CreatePhoto("b.heic") };

        var result = FileActions.MoveToStageFolder(photos, Path.Combine(_targetFolder, "PhotoRecull staged"), _targetFolder);

        Assert.Equal(0, result.SucceededCount);
        Assert.All(result.Failures, f => Assert.Equal(FileActions.StageFolderInsideTarget, f.Reason));
        Assert.All(photos, p => Assert.True(File.Exists(p.Path)));
    }

    [Theory]
    [InlineData(@"Z:\PhotoRecull staged", @"Z:\", true)]
    [InlineData(@"\\nas\home\Photos\staged", @"\\nas\home\Photos", true)]
    [InlineData(@"\\nas\home\Photos", @"\\nas\home\Photos\", true)]
    [InlineData(@"\\nas\home\PhotoRecull staged", @"\\nas\home\Photos", false)]
    [InlineData(@"D:\Pictures\Camera Roll staged", @"D:\Pictures\Camera Roll", false)]
    public void IsSameOrInside_ComparesWholeFolderNames(string folder, string parent, bool expected)
    {
        Assert.Equal(expected, FileActions.IsSameOrInside(folder, parent));
    }

    [Fact]
    public void MoveToStageFolder_ReportsEachPhotoWhereItWent_BeforeTheNextOneMoves()
    {
        var photos = new[] { CreatePhoto("a.heic"), CreatePhoto("b.heic") };
        var reported = new List<(RunFile File, bool NextStillInPlace)>();

        FileActions.MoveToStageFolder(photos, _stageFolder, _targetFolder,
            file => reported.Add((file, File.Exists(photos[1].Path))));

        Assert.Equal(
            [
                (new RunFile(photos[0].Path, Path.Combine(_stageFolder, "a.heic"), null, null, true, null, "a.heic".Length), true),
                (new RunFile(photos[1].Path, Path.Combine(_stageFolder, "b.heic"), null, null, true, null, "b.heic".Length), false),
            ],
            reported);
    }

    [Fact]
    public void DeletePermanently_ReportsAFailedPhotoWithItsReason()
    {
        var missing = new PhotoFile(Path.Combine(_targetFolder, "gone.heic"), "gone.heic",
            DateTimeOffset.UtcNow, IsIphone: true, TimestampSource: "filename");
        var reported = new List<RunFile>();

        FileActions.DeletePermanently([missing], reported.Add);

        Assert.Equal([new RunFile(missing.Path, null, null, null, false, "Missing: moved or deleted since the scan", 0)], reported);
    }

    [Fact]
    public void MoveToStageFolder_NeverOverwritesAndContinuesAfterAFailure()
    {
        Directory.CreateDirectory(_stageFolder);
        File.WriteAllText(Path.Combine(_stageFolder, "a.heic"), "already staged");
        var photos = new[] { CreatePhoto("a.heic"), CreatePhoto("b.heic") };

        var result = FileActions.MoveToStageFolder(photos, _stageFolder, _targetFolder);

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
        var failure = Assert.Single(result.Failures);
        Assert.Equal(locked, failure.Photo);
        Assert.Equal("Locked: another app or OneDrive sync is using it", failure.Reason);
        Assert.False(File.Exists(unlocked.Path));
    }

    [Fact]
    public void Results_CountTheBytesOfFilesThatSucceeded()
    {
        var photos = new[] { CreatePhoto("a.heic"), CreatePhoto("bb.heic") };

        var result = FileActions.DeletePermanently(photos);

        Assert.Equal("a.heic".Length + "bb.heic".Length, result.SucceededBytes);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void MissingFile_IsReportedAsMissing()
    {
        var missing = new PhotoFile(Path.Combine(_targetFolder, "gone.heic"), "gone.heic",
            DateTimeOffset.UtcNow, IsIphone: true, TimestampSource: "filename");

        var stageResult = FileActions.MoveToStageFolder([missing], _stageFolder, _targetFolder);
        var recycleResult = FileActions.MoveToRecycleBin([missing]);

        Assert.Equal("Missing: moved or deleted since the scan", Assert.Single(stageResult.Failures).Reason);
        Assert.Equal("Missing: moved or deleted since the scan", Assert.Single(recycleResult.Failures).Reason);
    }

    [Fact]
    public void MoveToStageFolder_UnusableFolder_FailsEveryPhotoWithoutThrowing()
    {
        var notAFolder = Path.Combine(_root, "taken-by-a-file");
        File.WriteAllText(notAFolder, "a file where the stage folder should go");
        var photos = new[] { CreatePhoto("a.heic"), CreatePhoto("b.heic") };

        var result = FileActions.MoveToStageFolder(photos, notAFolder, _targetFolder);

        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(2, result.Failures.Count);
        Assert.All(result.Failures, f => Assert.StartsWith("Can't use the stage folder", f.Reason));
        Assert.All(photos, p => Assert.True(File.Exists(p.Path)));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void MoveToRecycleBin_PutsTheFileInTheRecycleBin_OnCiOnly()
    {
        // Runs only in GitHub Actions: it really recycles a file, which on a
        // developer's PC would leave test files behind in their Recycle Bin.
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            return;
        var photo = CreatePhoto($"photorecull-recycle-test-{Guid.NewGuid():N}.heic");

        var result = FileActions.MoveToRecycleBin([photo]);

        Assert.Equal(1, result.SucceededCount);
        Assert.Empty(result.Failures);
        Assert.False(File.Exists(photo.Path));
        // Explorer may hide extensions in the Recycle Bin's display names.
        Assert.Contains(RecycleBinDisplayNames(), name => name.StartsWith(Path.GetFileNameWithoutExtension(photo.FileName)));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<string> RecycleBinDisplayNames()
    {
        const int RecycleBinFolder = 10;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        var names = new List<string>();
        foreach (dynamic item in shell.NameSpace(RecycleBinFolder).Items())
            names.Add((string)item.Name);
        return names;
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void HasRecycleBin_LocalDriveYes_NetworkShareNo()
    {
        Assert.True(FileActions.HasRecycleBin(Path.Combine(_targetFolder, "a.heic")));
        Assert.False(FileActions.HasRecycleBin(@"\\server\share\Camera Roll\a.heic"));
    }

    [Fact]
    public void MoveToStageFolder_ExplainsANameAlreadyInTheStageFolder()
    {
        Directory.CreateDirectory(_stageFolder);
        File.WriteAllText(Path.Combine(_stageFolder, "a.heic"), "already staged");

        var result = FileActions.MoveToStageFolder([CreatePhoto("a.heic")], _stageFolder, _targetFolder);

        Assert.Equal("A file with this name is already in the stage folder", Assert.Single(result.Failures).Reason);
    }
}
