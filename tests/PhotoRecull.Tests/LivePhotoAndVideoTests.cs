using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class LivePhotoAndVideoTests : IDisposable
{
    private static readonly string SamplePhoto = Path.Combine(AppContext.BaseDirectory, "TestData", "IMG_5587.HEIC");

    private readonly string _root = Directory.CreateTempSubdirectory("photorecull-live-").FullName;
    private readonly string _folder;

    public LivePhotoAndVideoTests() => _folder = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string PathOf(string fileName) => Path.Combine(_folder, fileName);

    private string CopySamplePhotoAs(string fileName)
    {
        File.Copy(SamplePhoto, PathOf(fileName));
        return PathOf(fileName);
    }

    private string WriteIphoneVideo(
        string fileName, string appleCreationDate, DateTime movieHeaderCreatedUtc, string? livePhotoIdentifier = null)
    {
        (string Key, string Value)[] metadata =
        [
            ("com.apple.quicktime.make", "Apple"),
            ("com.apple.quicktime.model", "iPhone 16 Pro"),
            ("com.apple.quicktime.creationdate", appleCreationDate),
        ];
        QuickTimeTestFile.Write(PathOf(fileName), movieHeaderCreatedUtc,
            livePhotoIdentifier is null ? metadata : [.. metadata, ("com.apple.quicktime.content.identifier", livePhotoIdentifier)]);
        return PathOf(fileName);
    }

    private const string LivePhotoIdentifier = "9B8F06F7-52F8-4238-BCC4-148AA5EE3FF8";

    // A Live Photo as an iPhone saves it: a still and a video named alike and
    // stamped with the same content identifier.
    private (string Still, string Video) WriteLivePhoto(string name)
    {
        var still = PathOf($"{name}.JPG");
        ExifTestFile.WriteIphoneJpeg(still, "2026:04:11 10:33:33", "+03:00", LivePhotoIdentifier);
        return (still, WriteIphoneVideo($"{name}.MOV", "2026-04-11T10:33:32+0300", new DateTime(2026, 4, 11, 7, 33, 35), LivePhotoIdentifier));
    }

    [Fact]
    public void ALivePhotosVideo_IsPartOfItsPhoto_NotAPhotoOfItsOwn()
    {
        var (still, video) = WriteLivePhoto("IMG_1776");

        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));

        Assert.Equal(still, photo.Path);
        Assert.True(photo.IsIphone);
        Assert.Equal(new DateTimeOffset(2026, 4, 11, 7, 33, 33, TimeSpan.Zero), photo.Timestamp);
        Assert.Equal(video, photo.LivePhotoVideoPath);
        Assert.Equal(new FileInfo(still).Length + new FileInfo(video).Length, photo.SizeBytes);
    }

    [Fact]
    public void AVideoNamedLikeAPhotoButNotItsLivePhoto_IsAPhotoOfItsOwn()
    {
        ExifTestFile.WriteIphoneJpeg(PathOf("IMG_1776.JPG"), "2026:04:11 10:33:33", "+03:00", LivePhotoIdentifier);
        WriteIphoneVideo("IMG_1776.MOV", "2027-02-01T09:00:00+0200", new DateTime(2027, 2, 1, 7, 0, 30), "11111111-2222-3333-4444-555555555555");
        CopySamplePhotoAs("IMG_5587.HEIC");
        WriteIphoneVideo("IMG_5587.MOV", "2026-04-11T10:33:32+0300", new DateTime(2026, 4, 11, 7, 33, 35), LivePhotoIdentifier);

        var photos = PhotoScanner.ScanFolder(_folder);

        Assert.Equal(["IMG_1776.JPG", "IMG_1776.MOV", "IMG_5587.HEIC", "IMG_5587.MOV"], photos.Select(p => p.FileName).Order());
        Assert.All(photos, p => Assert.Null(p.LivePhotoVideoPath));
    }

    [Fact]
    public void AVideoWithADifferentName_IsAPhotoOfItsOwn()
    {
        CopySamplePhotoAs("IMG_5587.HEIC");
        WriteIphoneVideo("IMG_5588.MOV", "2026-04-11T10:34:00+0300", new DateTime(2026, 4, 11, 7, 34, 10));

        var photos = PhotoScanner.ScanFolder(_folder);

        Assert.Equal(["IMG_5587.HEIC", "IMG_5588.MOV"], photos.Select(p => p.FileName).Order());
        Assert.All(photos, p => Assert.Null(p.LivePhotoVideoPath));
    }

    [Fact]
    public void AnIphoneVideo_IsTimedFromWhenRecordingStarted()
    {
        // A 40-minute video: the movie header holds when it was written, at the end.
        WriteIphoneVideo("IMG_4721.MOV", "2026-01-01T01:40:50+0200", new DateTime(2025, 12, 31, 22, 21, 39));

        var video = Assert.Single(PhotoScanner.ScanFolder(_folder));

        Assert.True(video.IsIphone);
        Assert.Equal("video", video.TimestampSource);
        Assert.Equal(new DateTimeOffset(2025, 12, 31, 23, 40, 50, TimeSpan.Zero), video.Timestamp);
    }

    [Fact]
    public void AVideoFromAnotherCamera_IsSkipped()
    {
        QuickTimeTestFile.Write(PathOf("clip.mp4"), new DateTime(2026, 1, 1, 12, 0, 0),
            ("com.apple.quicktime.creationdate", "2026-01-01T14:00:00+0200"));

        var video = Assert.Single(PhotoScanner.ScanFolder(_folder));

        Assert.False(video.IsIphone);
        Assert.Null(video.Timestamp);
    }

    [Fact]
    public void AnIphoneVideoWithoutItsRecordingStart_IsSkipped_RatherThanTimedFromItsEnd()
    {
        QuickTimeTestFile.Write(PathOf("IMG_4721.MOV"), new DateTime(2026, 1, 1, 0, 21, 39),
            ("com.apple.quicktime.make", "Apple"),
            ("com.apple.quicktime.model", "iPhone 16 Pro"));

        var video = Assert.Single(PhotoScanner.ScanFolder(_folder));

        Assert.True(video.IsIphone);
        Assert.Null(video.Timestamp);
        Assert.Equal([video], PhotoMatcher.Match([], [video]).Skipped);
    }

    [Theory]
    [InlineData("2025:06:22 20:12:16", null, "-04:00", "2025-06-23T00:12:16.000Z")]
    [InlineData("2026:04:11 10:33:33", "208", "+03:00", "2026-04-11T07:33:33.208Z")]
    [InlineData("2026:04:11 10:33:33", "2", "+03:00", "2026-04-11T07:33:33.200Z")]
    [InlineData("2026:04:11 10:33:33", null, "+05:30", "2026-04-11T05:03:33.000Z")]
    [InlineData("2026:04:11 10:33:33", null, null, "2026-04-11T10:33:33.000Z")]
    public void ExifCaptureTime_AppliesTheOffsetWhereThePhotoWasTaken(string taken, string? subSecond, string? offset, string expectedUtc)
    {
        Assert.Equal(DateTimeOffset.Parse(expectedUtc), PhotoScanner.ExifCaptureTime(taken, subSecond, offset));
    }

    [Fact]
    public void StagingALivePhoto_MovesItsVideoToo()
    {
        WriteLivePhoto("IMG_1776");
        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));
        var stageFolder = Path.Combine(_root, "_staged");

        var result = FileActions.MoveToStageFolder([photo], stageFolder, _folder);

        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(photo.SizeBytes, result.SucceededBytes);
        Assert.Equal(["IMG_1776.JPG", "IMG_1776.MOV"], Directory.GetFiles(stageFolder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void StagingALivePhotoWhoseNameIsTaken_LeavesBothPartsWhereTheyAre()
    {
        var (still, video) = WriteLivePhoto("IMG_1776");
        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));
        var stageFolder = Directory.CreateDirectory(Path.Combine(_root, "_staged")).FullName;
        File.WriteAllText(Path.Combine(stageFolder, "IMG_1776.JPG"), "staged before");

        var result = FileActions.MoveToStageFolder([photo], stageFolder, _folder);

        Assert.Equal("A file with this name is already in the stage folder", Assert.Single(result.Failures).Reason);
        Assert.True(File.Exists(still));
        Assert.True(File.Exists(video));
        Assert.False(File.Exists(Path.Combine(stageFolder, "IMG_1776.MOV")));
    }

    [Fact]
    public void StagingPhotosNamedAlikeFromDifferentMonths_KeepsThemInTheirMonthFolders()
    {
        var backup = Directory.CreateDirectory(PathOf("backup")).FullName;
        var photos = new[] { @"2025\12\IMG_0001.HEIC", @"2026\01\IMG_0001.HEIC" }.Select(relative =>
        {
            var path = Path.Combine(backup, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, relative);
            return new PhotoFile(path, "IMG_0001.HEIC", DateTimeOffset.UtcNow, IsIphone: true, TimestampSource: "exif");
        }).ToList();
        var stageFolder = Path.Combine(_root, "_staged");

        var result = FileActions.MoveToStageFolder(photos, stageFolder, backup);

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(@"2025\12\IMG_0001.HEIC", File.ReadAllText(Path.Combine(stageFolder, @"2025\12\IMG_0001.HEIC")));
        Assert.Equal(@"2026\01\IMG_0001.HEIC", File.ReadAllText(Path.Combine(stageFolder, @"2026\01\IMG_0001.HEIC")));
    }

    [Fact]
    public void DeletingALivePhotoWhosePhotoIsLocked_CountsItsDeletedVideoAsFreed()
    {
        var (still, video) = WriteLivePhoto("IMG_1776");
        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));
        long videoBytes = new FileInfo(video).Length;

        FileActionResult result;
        using (File.Open(still, FileMode.Open, FileAccess.Read, FileShare.None))
            result = FileActions.DeletePermanently([photo]);

        Assert.Equal(0, result.SucceededCount);
        Assert.Single(result.Failures);
        Assert.False(File.Exists(video));
        Assert.Equal(videoBytes, result.SucceededBytes);
    }

    [Fact]
    public void DeletingALivePhoto_DeletesItsVideoToo_AndARetryAfterTheVideoWentStillDeletesThePhoto()
    {
        var (still, video) = WriteLivePhoto("IMG_1776");
        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));
        File.Delete(video);

        var result = FileActions.DeletePermanently([photo]);

        Assert.Equal(1, result.SucceededCount);
        Assert.Empty(result.Failures);
        Assert.False(File.Exists(still));
    }
}
