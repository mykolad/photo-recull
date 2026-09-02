using PhotoCleanup;

namespace PhotoCleanup.Tests;

/// <summary>
/// Tests for OneDriveScanner using the two real sample HEIC files in TestData/.
///
/// Both files are the same photo — one named in OneDrive iOS format
/// (timestamp-based, UTC), the other in the original iPhone format (IMG_xxxx).
/// Both should resolve to the same UTC capture time.
/// </summary>
public class OneDriveScannerTests
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
        var photos = OneDriveScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.EndsWith("_iOS.heic", StringComparison.OrdinalIgnoreCase));

        Assert.True(photo.IsIphone);
    }

    [Fact]
    public void ScanFolder_IosCameraFile_TimestampParsedFromFilename()
    {
        var photos = OneDriveScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.EndsWith("_iOS.heic", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("filename", photo.TimestampSource);
        Assert.NotNull(photo.Timestamp);
    }

    [Fact]
    public void ScanFolder_IosCameraFile_TimestampIsCorrectUtc()
    {
        var photos = OneDriveScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.EndsWith("_iOS.heic", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ExpectedUtc, photo.Timestamp!.Value);
    }

    // -----------------------------------------------------------------------
    // iPhone detection — EXIF fallback (Google Photos download / iCloud format)
    // -----------------------------------------------------------------------

    [Fact]
    public void ScanFolder_ImgFile_IsDetectedAsIphoneViaExif()
    {
        var photos = OneDriveScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.Equals("IMG_5587.HEIC", StringComparison.OrdinalIgnoreCase));

        Assert.True(photo.IsIphone);
    }

    [Fact]
    public void ScanFolder_ImgFile_TimestampParsedFromExif()
    {
        var photos = OneDriveScanner.ScanFolder(TestDataDir);
        var photo = photos.Single(p => p.FileName.Equals("IMG_5587.HEIC", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("exif", photo.TimestampSource);
        Assert.NotNull(photo.Timestamp);
    }

    [Fact]
    public void ScanFolder_ImgFile_TimestampIsCorrectUtc()
    {
        var photos = OneDriveScanner.ScanFolder(TestDataDir);
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
        var photos = OneDriveScanner.ScanFolder(TestDataDir);

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
            () => OneDriveScanner.ScanFolder(@"C:\does\not\exist"));
    }
}
