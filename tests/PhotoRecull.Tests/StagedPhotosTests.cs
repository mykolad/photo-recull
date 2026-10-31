using PhotoRecull.Core;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.Tests;

public class StagedPhotosTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("photorecull-staged-").FullName;
    private string Target => Path.Combine(_root, "Camera Roll");
    private string Stage => Path.Combine(_root, "_staged");
    private TargetSettings OneDrive => new(TargetKind.OneDrive, Target);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string FileAt(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Path.GetFileName(path));
        return path;
    }

    private CleanupRun StageRun => new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, CleanupAction.Staged, OneDrive, Stage, 0, 0, 0);

    [Fact]
    public void StillStaged_ListsTheRecordedPhotosThatAreStillInTheStageFolder()
    {
        var stillThere = FileAt(Path.Combine(Stage, @"2026\10\IMG_1.HEIC"));
        var itsVideo = FileAt(Path.Combine(Stage, @"2026\10\IMG_1.MOV"));
        RunFile[] files =
        [
            new(Path.Combine(Target, @"2026\10\IMG_1.HEIC"), stillThere, Path.Combine(Target, @"2026\10\IMG_1.MOV"), itsVideo, true, null, 10),
            new(Path.Combine(Target, @"2026\10\IMG_2.HEIC"), Path.Combine(Stage, @"2026\10\IMG_2.HEIC"), null, null, true, null, 10),
            new(Path.Combine(Target, @"2026\10\IMG_3.HEIC"), null, null, null, false, "Locked", 0),
        ];

        var staged = Assert.Single(StagedPhotos.StillStaged([(StageRun, files.ToList())]));

        Assert.Equal((stillThere, Path.Combine(Target, @"2026\10\IMG_1.HEIC")), (staged.StagedPath, staged.OriginalPath));
        Assert.Equal((itsVideo, Path.Combine(Target, @"2026\10\IMG_1.MOV")), (staged.StagedVideoPath, staged.OriginalVideoPath));
    }

    [Fact]
    public void StillStaged_ForARunFromBeforePhotosWereRecorded_ReadsItsStageFolder()
    {
        var staged = FileAt(Path.Combine(Stage, @"2026\10\IMG_1.HEIC"));
        FileAt(Path.Combine(Stage, "notes.txt"));

        var photo = Assert.Single(StagedPhotos.StillStaged([(StageRun, new List<RunFile>())]));

        Assert.Equal((staged, Path.Combine(Target, @"2026\10\IMG_1.HEIC")), (photo.StagedPath, photo.OriginalPath));
    }

    [Fact]
    public void MoveBack_PutsAPhotoAndItsVideoWhereTheyWere_AndNeverOverwrites()
    {
        var photo = new StagedPhoto(OneDrive,
            FileAt(Path.Combine(Stage, @"2026\10\IMG_1.HEIC")), Path.Combine(Target, @"2026\10\IMG_1.HEIC"),
            FileAt(Path.Combine(Stage, @"2026\10\IMG_1.MOV")), Path.Combine(Target, @"2026\10\IMG_1.MOV"));
        var taken = new StagedPhoto(OneDrive,
            FileAt(Path.Combine(Stage, @"2026\10\IMG_2.HEIC")), FileAt(Path.Combine(Target, @"2026\10\IMG_2.HEIC")), null, null);
        var recorded = new List<RunFile>();

        var result = FileActions.MoveBack([photo, taken], recorded.Add);

        Assert.Equal(1, result.SucceededCount);
        Assert.True(File.Exists(photo.OriginalPath));
        Assert.True(File.Exists(photo.OriginalVideoPath));
        Assert.False(File.Exists(photo.StagedPath));
        Assert.Equal("A file with this name is already back in its place", Assert.Single(result.Failures).Reason);
        Assert.True(File.Exists(taken.StagedPath));
        Assert.Equal([photo.OriginalPath, null], recorded.Select(r => r.Destination));
    }
}
