using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class CleanupScannerTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

    // Both sample files are the same photo: 2026-04-11 07:33:33.208 UTC.
    private static readonly DateTimeOffset SamplePhotoTime = new(2026, 4, 11, 7, 33, 33, 208, TimeSpan.Zero);

    [Fact]
    public void PickedOnce_OneCopyKeptAndTheOtherBecomesACandidate()
    {
        var result = CleanupScanner.ScanAndMatch(new OneDriveTarget(TestDataDir), [SamplePhotoTime],
            new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), TimeZoneInfo.Utc);

        Assert.Equal(2, result.PhotoFilesFound);
        Assert.Equal(2, result.IphonePhotosInRange);
        Assert.Single(result.Match.Matched);
        Assert.Single(result.Match.Unmatched);
    }

    [Fact]
    public void RangeExcludesThePhotos_NothingInRange()
    {
        var result = CleanupScanner.ScanAndMatch(new OneDriveTarget(TestDataDir), [],
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), TimeZoneInfo.Utc);

        Assert.Equal(2, result.PhotoFilesFound);
        Assert.Equal(0, result.IphonePhotosInRange);
    }
}
