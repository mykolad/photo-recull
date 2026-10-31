using PhotoRecull.Core;

namespace PhotoRecull.Tests;

/// <summary>
/// Tests for PhotoScanner using the two real sample HEIC files in TestData/.
///
/// Both files are the same photo — one named in OneDrive iOS format
/// (timestamp-based, UTC), the other in the original iPhone format (IMG_xxxx).
/// Both should resolve to the same UTC capture time.
/// </summary>
public class PhotoScannerTests
{
    // TestData folder is copied next to the test DLL by the .csproj ItemGroup.
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    private static readonly string IosCameraFile =
        Path.Combine(TestDataDir, "20260411_073333208_iOS.heic");

    private static readonly string GooglePhotosFile =
        Path.Combine(TestDataDir, "IMG_5587.HEIC");

    // Known UTC capture time for both sample files:
    // OneDrive filename encodes UTC 07:33:33.208; EXIF has local 10:33:33 +03:00.
    private static readonly DateTimeOffset ExpectedUtc =
        new(2026, 4, 11, 7, 33, 33, 208, TimeSpan.Zero);

    // -----------------------------------------------------------------------
    // iPhone detection — OneDrive filename pattern
    // -----------------------------------------------------------------------

    [Fact]
    public void ScanFolder_IosCameraFile_IsDetectedAsIphone()
    {
        var photos = PhotoScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.EndsWith("_iOS.heic", StringComparison.OrdinalIgnoreCase));

        Assert.True(photo.IsIphone);
    }

    [Fact]
    public void ScanFolder_IosCameraFile_TimestampParsedFromFilename()
    {
        var photos = PhotoScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.EndsWith("_iOS.heic", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("filename", photo.TimestampSource);
        Assert.False(photo.TimestampInWholeSeconds);
        Assert.NotNull(photo.Timestamp);
    }

    [Fact]
    public void ScanFolder_IosCameraFile_TimestampIsCorrectUtc()
    {
        var photos = PhotoScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.EndsWith("_iOS.heic", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ExpectedUtc, photo.Timestamp!.Value);
    }

    // -----------------------------------------------------------------------
    // iPhone detection — EXIF fallback (Google Photos download / iCloud format)
    // -----------------------------------------------------------------------

    [Fact]
    public void ScanFolder_ImgFile_IsDetectedAsIphoneViaExif()
    {
        var photos = PhotoScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.Equals("IMG_5587.HEIC", StringComparison.OrdinalIgnoreCase));

        Assert.True(photo.IsIphone);
    }

    [Fact]
    public void ScanFolder_ImgFile_TimestampParsedFromExif()
    {
        var photos = PhotoScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.Equals("IMG_5587.HEIC", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("exif", photo.TimestampSource);
        Assert.False(photo.TimestampInWholeSeconds);
        Assert.NotNull(photo.Timestamp);
    }

    [Fact]
    public void ScanFolder_ImgFile_TimestampIsCorrectUtc()
    {
        var photos = PhotoScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.Equals("IMG_5587.HEIC", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ExpectedUtc, photo.Timestamp!.Value);
    }

    // -----------------------------------------------------------------------
    // Cross-file consistency — same photo must yield the same UTC timestamp
    // regardless of which filename format it has.
    // -----------------------------------------------------------------------

    [Fact]
    public void ScanFolder_BothSampleFiles_ProduceSameUtcTimestamp()
    {
        var photos = PhotoScanner.ScanFolder(TestDataDir);

        var ios = photos.Single(p => p.FileName.EndsWith("_iOS.heic", StringComparison.OrdinalIgnoreCase));
        var img = photos.Single(p => p.FileName.Equals("IMG_5587.HEIC", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(ios.Timestamp);
        Assert.NotNull(img.Timestamp);
        Assert.Equal(ios.Timestamp!.Value, img.Timestamp!.Value);
    }

    // -----------------------------------------------------------------------
    // Folder validation
    // -----------------------------------------------------------------------

    [Fact]
    public void ScanFolder_NonExistentFolder_ThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => PhotoScanner.ScanFolder(@"C:\does\not\exist"));
    }

    // -----------------------------------------------------------------------
    // Progress and cancellation (used by the app's Scan screen)
    // -----------------------------------------------------------------------

    [Fact]
    public void ScanFolder_ReportsEveryPhotoFileBeingReadWithProgressThroughTheFolder()
    {
        var reports = new List<ScanProgress>();

        PhotoScanner.ScanFolder(TestDataDir, new SynchronousProgress<ScanProgress>(reports.Add));

        Assert.Equal([IosCameraFile, GooglePhotosFile],
            reports.Select(r => r.CurrentFile).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(reports, r => Assert.Equal(2, r.FilesTotal));
        Assert.Equal([1, 2], reports.Select(r => r.FilesChecked));
    }

    [Fact]
    public void ScanFolder_RecordsEachFilesSize()
    {
        var photos = PhotoScanner.ScanFolder(TestDataDir);

        Assert.All(photos, p => Assert.Equal(new FileInfo(p.Path).Length, p.SizeBytes));
    }

    [Fact]
    public void ScanFolder_AlreadyCancelled_Throws()
    {
        Assert.Throws<OperationCanceledException>(
            () => PhotoScanner.ScanFolder(TestDataDir, cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void OneDriveTarget_ScansItsCameraRollFolder()
    {
        var target = new OneDriveTarget(TestDataDir);

        Assert.Equal("OneDrive", target.DisplayName);
        Assert.Equal(2, target.Scan(firstDay: null, lastDay: null).Count);
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
