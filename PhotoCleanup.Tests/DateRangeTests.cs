using PhotoCleanup;

namespace PhotoCleanup.Tests;

/// <summary>
/// Tests that the date range filter correctly includes/excludes OneDrive photos
/// and that the HTML report generator produces valid output.
/// </summary>
public class DateRangeTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    // The two sample files were taken on 2026-04-11 07:33:33 UTC
    private static readonly DateOnly PhotoDate = new(2026, 4, 11);

    // -----------------------------------------------------------------------
    // Date range filtering (applied in Program.cs after ScanFolder)
    // We test the filtering logic directly here using real scan results.
    // -----------------------------------------------------------------------

    [Fact]
    public void DateFilter_RangeCoversPhotoDate_BothFilesIncluded()
    {
        var all = OneDriveScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        // Both sample files have iPhone=true and fall within April 2026
        Assert.Equal(2, filtered.Count(p => p.IsIphone));
    }

    [Fact]
    public void DateFilter_RangeBeforePhotoDate_IphoneFilesExcluded()
    {
        var all = OneDriveScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        // No iPhone files should survive (photo is April, not March)
        Assert.DoesNotContain(filtered, p => p.IsIphone);
    }

    [Fact]
    public void DateFilter_RangeAfterPhotoDate_IphoneFilesExcluded()
    {
        var all = OneDriveScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        Assert.DoesNotContain(filtered, p => p.IsIphone);
    }

    [Fact]
    public void DateFilter_ExactDayMatch_BothFilesIncluded()
    {
        var all = OneDriveScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, PhotoDate, PhotoDate);

        Assert.Equal(2, filtered.Count(p => p.IsIphone));
    }

    [Fact]
    public void DateFilter_DayBeforePhoto_IphoneFilesExcluded()
    {
        var all = OneDriveScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 10));

        Assert.DoesNotContain(filtered, p => p.IsIphone);
    }

    [Fact]
    public void DateFilter_NonIphoneFiles_AlwaysIncluded()
    {
        // Simulate a scan result that contains a non-iPhone file
        var nonIphone = new OneDrivePhoto(
            Path: @"C:\fake\DSC001.jpg", FileName: "DSC001.jpg",
            Timestamp: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            IsIphone: false, TimestampSource: null);

        var photos = new List<OneDrivePhoto> { nonIphone };
        // Range doesn't include the timestamp, but non-iPhone should still appear
        var filtered = ApplyDateFilter(photos, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        Assert.Single(filtered);
    }

    // -----------------------------------------------------------------------
    // HTML report generator
    // -----------------------------------------------------------------------

    [Fact]
    public void HtmlReport_GeneratesFile_WithExpectedSections()
    {
        var tmpPath = Path.GetTempFileName() + ".html";
        try
        {
            var photos = OneDriveScanner.ScanFolder(TestDataDir);
            // Simulate: one photo matched (keep), one unmatched (remove)
            var ios = photos.First(p => p.FileName.EndsWith("_iOS.heic", StringComparison.OrdinalIgnoreCase));
            var img = photos.First(p => p.FileName.Equals("IMG_5587.HEIC", StringComparison.OrdinalIgnoreCase));

            var fakeResult = new MatchResult(
                Matched:   [img],
                Unmatched: [ios],
                Skipped:   []);

            HtmlReportGenerator.Write(tmpPath, fakeResult, TestDataDir,
                fromDate: new DateOnly(2026, 4, 1),
                toDate:   new DateOnly(2026, 4, 30));

            var html = File.ReadAllText(tmpPath);

            Assert.Contains("<!DOCTYPE html>", html);
            Assert.Contains("To Remove", html);
            Assert.Contains("To Keep",   html);
            Assert.Contains(ios.FileName, html);
            Assert.Contains(img.FileName, html);
            // HEIC files are decoded to embedded JPEG thumbnails (browsers can't render HEIC)
            Assert.Contains("data:image/jpeg;base64,", html);
            // Date range label in output
            Assert.Contains("2026-04-01", html);
            Assert.Contains("2026-04-30", html);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Fact]
    public void HtmlReport_NoDateRange_ShowsEntireLibraryLabel()
    {
        var tmpPath = Path.GetTempFileName() + ".html";
        try
        {
            var emptyResult = new MatchResult([], [], []);
            HtmlReportGenerator.Write(tmpPath, emptyResult, @"C:\fake", null, null);

            var html = File.ReadAllText(tmpPath);
            Assert.Contains("entire library", html);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    // -----------------------------------------------------------------------
    // Helper — mirrors the date-range filter logic from Program.cs
    // -----------------------------------------------------------------------

    private static List<OneDrivePhoto> ApplyDateFilter(
        List<OneDrivePhoto> photos, DateOnly from, DateOnly to)
    {
        var fromUtc = new DateTimeOffset(from, TimeOnly.MinValue, TimeSpan.Zero);
        var toUtc   = new DateTimeOffset(to,   TimeOnly.MaxValue, TimeSpan.Zero);

        return photos
            .Where(p => !p.IsIphone
                     || !p.Timestamp.HasValue
                     || (p.Timestamp.Value >= fromUtc && p.Timestamp.Value <= toUtc))
            .ToList();
    }
}
