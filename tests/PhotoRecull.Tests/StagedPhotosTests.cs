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

    private string InTarget(string name) => Path.Combine(Target, @"2026\10", name);
    private string InStage(string name) => Path.Combine(Stage, @"2026\10", name);

    // A run whose photos were recorded one by one, as runs are now.
    private CleanupRun RecordedStageRun(int minute = 0) =>
        new(Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddMinutes(minute), CleanupAction.Staged, OneDrive, Stage, 0, 0, 0)
        {
            PhotosRecorded = true,
        };

    [Fact]
    public void StillStaged_ListsTheRecordedPhotosThatAreStillInTheStageFolder()
    {
        var stillThere = FileAt(InStage("IMG_1.HEIC"));
        var itsVideo = FileAt(InStage("IMG_1.MOV"));
        RunFile[] files =
        [
            new(InTarget("IMG_1.HEIC"), stillThere, InTarget("IMG_1.MOV"), itsVideo, true, null, 10),
            new(InTarget("IMG_2.HEIC"), InStage("IMG_2.HEIC"), null, null, true, null, 10),
            new(InTarget("IMG_3.HEIC"), null, null, null, false, "Locked", 0),
        ];

        var inventory = StagedPhotos.StillStaged([(RecordedStageRun(), files.ToList())]);

        var staged = Assert.Single(inventory.Photos);
        Assert.Equal((stillThere, InTarget("IMG_1.HEIC")), (staged.StagedPath, staged.OriginalPath));
        Assert.Equal((itsVideo, InTarget("IMG_1.MOV")), (staged.StagedVideoPath, staged.OriginalVideoPath));
        Assert.Empty(inventory.UnreachableFolders);
    }

    [Fact]
    public void StillStaged_PutsALivePhotoBackTogether_AfterARetryStagedItsPhoto()
    {
        var video = FileAt(InStage("IMG_1.MOV"));
        var still = FileAt(InStage("IMG_1.HEIC"));
        var videoFirst = new RunFile(InTarget("IMG_1.HEIC"), null, InTarget("IMG_1.MOV"), video, false, "Locked", 5);
        var stillOnRetry = new RunFile(InTarget("IMG_1.HEIC"), still, InTarget("IMG_1.MOV"), null, true, null, 10);

        var photo = Assert.Single(StagedPhotos.StillStaged(
            [(RecordedStageRun(0), [videoFirst]), (RecordedStageRun(1), [stillOnRetry])]).Photos);

        Assert.Equal((still, video), (photo.StagedPath, photo.StagedVideoPath));
    }

    [Fact]
    public void StillStaged_KeepsAStagedVideoWhosePhotoWasMovedAwayByHand()
    {
        var video = FileAt(InStage("IMG_1.MOV"));
        var file = new RunFile(InTarget("IMG_1.HEIC"), InStage("IMG_1.HEIC"), InTarget("IMG_1.MOV"), video, true, null, 10);

        var photo = Assert.Single(StagedPhotos.StillStaged([(RecordedStageRun(), [file])]).Photos);

        Assert.Equal((video, InTarget("IMG_1.MOV"), (string?)null), (photo.StagedPath, photo.OriginalPath, photo.StagedVideoPath));
    }

    [Fact]
    public void StillStaged_CantSayNothingIsLeft_WhenAStageFolderCantBeReached()
    {
        var unreachable = RecordedStageRun() with { Destination = @"\\nowhere-photorecull-test\share\_staged" };
        var file = new RunFile(InTarget("IMG_1.HEIC"), @"\\nowhere-photorecull-test\share\_staged\IMG_1.HEIC", null, null, true, null, 10);

        var inventory = StagedPhotos.StillStaged([(unreachable, [file])]);

        Assert.Empty(inventory.Photos);
        Assert.Equal([@"\\nowhere-photorecull-test\share\_staged"], inventory.UnreachableFolders);
        Assert.False(inventory.IsVerifiedEmpty);
    }

    [Fact]
    public void StillStaged_ForAStageFolderRemovedByHand_IsVerifiedEmpty()
    {
        var file = new RunFile(InTarget("IMG_1.HEIC"), InStage("IMG_1.HEIC"), null, null, true, null, 10);

        Assert.True(StagedPhotos.StillStaged([(RecordedStageRun(), [file])]).IsVerifiedEmpty);
    }

    [Fact]
    public void StillStaged_ForARunFromBeforePhotosWereRecorded_ReadsItsStageFolder_PairingLivePhotos()
    {
        var still = InStage("IMG_1776.JPG");
        Directory.CreateDirectory(Path.GetDirectoryName(still)!);
        ExifTestFile.WriteIphoneJpeg(still, "2026:10:11 10:33:33", "+03:00", "9B8F06F7-52F8-4238-BCC4-148AA5EE3FF8");
        var video = InStage("IMG_1776.MOV");
        QuickTimeTestFile.Write(video, new DateTime(2026, 10, 11, 7, 33, 35),
            ("com.apple.quicktime.content.identifier", "9B8F06F7-52F8-4238-BCC4-148AA5EE3FF8"));
        FileAt(Path.Combine(Stage, "notes.txt"));
        var legacyRun = RecordedStageRun() with { PhotosRecorded = false };

        var photo = Assert.Single(StagedPhotos.StillStaged([(legacyRun, [])]).Photos);

        Assert.Equal((still, InTarget("IMG_1776.JPG")), (photo.StagedPath, photo.OriginalPath));
        Assert.Equal((video, InTarget("IMG_1776.MOV")), (photo.StagedVideoPath, photo.OriginalVideoPath));
    }

    [Fact]
    public void StillStaged_ForARunInterruptedBeforeItsFirstPhoto_DoesntReadItsStageFolder()
    {
        FileAt(InStage("OTHER_CLEANUP.HEIC"));
        var interrupted = RecordedStageRun() with { Status = RunStatus.Interrupted };

        Assert.Empty(StagedPhotos.StillStaged([(interrupted, [])]).Photos);
    }

    [Fact]
    public void MoveBack_PutsAPhotoAndItsVideoWhereTheyWere_AndNeverOverwrites()
    {
        var photo = new StagedPhoto(OneDrive,
            FileAt(InStage("IMG_1.HEIC")), InTarget("IMG_1.HEIC"),
            FileAt(InStage("IMG_1.MOV")), InTarget("IMG_1.MOV"));
        var taken = new StagedPhoto(OneDrive, FileAt(InStage("IMG_2.HEIC")), FileAt(InTarget("IMG_2.HEIC")), null, null);
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
