using PhotoRecull.Cli;
using PhotoRecull.Core;

namespace PhotoRecull.Tests;

/// <summary>
/// Tests that the date range filter correctly includes/excludes target photos
/// and that the HTML report generator produces valid output.
/// </summary>
public class DateRangeTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    // The two sample files were taken on 2026-04-11 07:33:33 UTC
    private static readonly DateOnly PhotoDate = new(2026, 4, 11);

    // -----------------------------------------------------------------------
    // Date range filtering, using real scan results
    // -----------------------------------------------------------------------

    [Fact]
    public void DateFilter_RangeCoversPhotoDate_BothFilesIncluded()
    {
        var all = PhotoScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        // Both sample files have iPhone=true and fall within April 2026
        Assert.Equal(2, filtered.Count(p => p.IsIphone));
    }

    [Fact]
    public void DateFilter_RangeBeforePhotoDate_IphoneFilesExcluded()
    {
        var all = PhotoScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        // No iPhone files should survive (photo is April, not March)
        Assert.DoesNotContain(filtered, p => p.IsIphone);
    }

    [Fact]
    public void DateFilter_RangeAfterPhotoDate_IphoneFilesExcluded()
    {
        var all = PhotoScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        Assert.DoesNotContain(filtered, p => p.IsIphone);
    }

    [Fact]
    public void DateFilter_ExactDayMatch_BothFilesIncluded()
    {
        var all = PhotoScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, PhotoDate, PhotoDate);

        Assert.Equal(2, filtered.Count(p => p.IsIphone));
    }

    [Fact]
    public void DateFilter_DayBeforePhoto_IphoneFilesExcluded()
    {
        var all = PhotoScanner.ScanFolder(TestDataDir);
        var filtered = ApplyDateFilter(all, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 10));

        Assert.DoesNotContain(filtered, p => p.IsIphone);
    }

    [Fact]
    public void DateFilter_NonIphoneFiles_AlwaysIncluded()
    {
        // Simulate a scan result that contains a non-iPhone file
        var nonIphone = new PhotoFile(
            Path: @"C:\fake\DSC001.jpg", FileName: "DSC001.jpg",
            Timestamp: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            IsIphone: false, TimestampSource: null);

        var photos = new List<PhotoFile> { nonIphone };
        // Range doesn't include the timestamp, but non-iPhone should still appear
        var filtered = ApplyDateFilter(photos, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        Assert.Single(filtered);
    }

    [Fact]
    public void DateFilter_WithTimeZone_UsesLocalCalendarDays()
    {
        // 2026-07-31 22:30 UTC is already 1 Aug in UTC+3.
        var lateNight = new PhotoFile(@"C:\fake\IMG_1.HEIC", "IMG_1.HEIC",
            new DateTimeOffset(2026, 7, 31, 22, 30, 0, TimeSpan.Zero), IsIphone: true, TimestampSource: "exif");
        var utcPlus3 = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

        var inAugustLocally = DateRangeFilter.KeepPhotosCapturedInRange(
            [lateNight], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), utcPlus3);
        var inAugustUtc = DateRangeFilter.KeepPhotosCapturedInRange(
            [lateNight], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.Single(inAugustLocally);
        Assert.Empty(inAugustUtc);
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
            var photos = PhotoScanner.ScanFolder(TestDataDir);
            // Simulate: one photo matched (keep), one unmatched (remove)
            var ios = photos.First(p => p.FileName.EndsWith("_iOS.heic", StringComparison.OrdinalIgnoreCase));
            var img = photos.First(p => p.FileName.Equals("IMG_5587.HEIC", StringComparison.OrdinalIgnoreCase));

            var fakeResult = new MatchResult(
                Matched:   [img],
                Unmatched: [ios],
                Skipped:   []);

            HtmlReportGenerator.Write(tmpPath, fakeResult, "OneDrive", TestDataDir,
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
            HtmlReportGenerator.Write(tmpPath, emptyResult, "OneDrive", @"C:\fake", null, null);

            var html = File.ReadAllText(tmpPath);
            Assert.Contains("entire library", html);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    private static List<PhotoFile> ApplyDateFilter(List<PhotoFile> photos, DateOnly from, DateOnly to) =>
        DateRangeFilter.KeepPhotosCapturedInRange(photos, from, to);
}
