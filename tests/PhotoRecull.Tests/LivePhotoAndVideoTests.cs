using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class LivePhotoAndVideoTests : IDisposable
{
    private static readonly string SamplePhoto = Path.Combine(AppContext.BaseDirectory, "TestData", "IMG_5587.HEIC");

    private readonly string _folder = Directory.CreateTempSubdirectory("photorecull-live-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string PathOf(string fileName) => Path.Combine(_folder, fileName);

    private string CopySamplePhotoAs(string fileName)
    {
        File.Copy(SamplePhoto, PathOf(fileName));
        return PathOf(fileName);
    }

    private string WriteIphoneVideo(string fileName, string appleCreationDate, DateTime movieHeaderCreatedUtc)
    {
        QuickTimeTestFile.Write(PathOf(fileName), movieHeaderCreatedUtc,
            ("com.apple.quicktime.make", "Apple"),
            ("com.apple.quicktime.model", "iPhone 16 Pro"),
            ("com.apple.quicktime.creationdate", appleCreationDate));
        return PathOf(fileName);
    }

    [Fact]
    public void ALivePhotosVideo_IsPartOfItsPhoto_NotAPhotoOfItsOwn()
    {
        var still = CopySamplePhotoAs("IMG_5587.HEIC");
        var video = WriteIphoneVideo("IMG_5587.MOV", "2026-04-11T10:33:32+0300", new DateTime(2026, 4, 11, 7, 33, 35));

        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));

        Assert.Equal(still, photo.Path);
        Assert.Equal(video, photo.LivePhotoVideoPath);
        Assert.Equal(new FileInfo(still).Length + new FileInfo(video).Length, photo.SizeBytes);
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
    public void VideoCaptureTime_FallsBackToTheMovieHeader_ReadAsUtc()
    {
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 21, 39, TimeSpan.Zero),
            PhotoScanner.VideoCaptureTime(appleCreationDate: null, new DateTime(2026, 1, 1, 0, 21, 39, DateTimeKind.Unspecified)));
        Assert.Null(PhotoScanner.VideoCaptureTime(null, null));
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
        CopySamplePhotoAs("IMG_5587.HEIC");
        WriteIphoneVideo("IMG_5587.MOV", "2026-04-11T10:33:32+0300", new DateTime(2026, 4, 11, 7, 33, 35));
        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));
        var stageFolder = PathOf("_staged");

        var result = FileActions.MoveToStageFolder([photo], stageFolder);

        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(photo.SizeBytes, result.SucceededBytes);
        Assert.Equal(["IMG_5587.HEIC", "IMG_5587.MOV"], Directory.GetFiles(stageFolder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void DeletingALivePhoto_DeletesItsVideoToo_AndARetryAfterTheVideoWentStillDeletesThePhoto()
    {
        var still = CopySamplePhotoAs("IMG_5587.HEIC");
        var video = WriteIphoneVideo("IMG_5587.MOV", "2026-04-11T10:33:32+0300", new DateTime(2026, 4, 11, 7, 33, 35));
        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));
        File.Delete(video);

        var result = FileActions.DeletePermanently([photo]);

        Assert.Equal(1, result.SucceededCount);
        Assert.Empty(result.Failures);
        Assert.False(File.Exists(still));
    }
}
