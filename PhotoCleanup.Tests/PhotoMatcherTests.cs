using PhotoCleanup;

namespace PhotoCleanup.Tests;

/// <summary>
/// Unit tests for PhotoMatcher.Match.
/// These tests use synthetic OneDrivePhoto records — no real files needed.
/// </summary>
public class PhotoMatcherTests
{
    // Arbitrary base time used across tests
    private static readonly DateTimeOffset Base =
        new(2026, 4, 11, 7, 33, 33, 0, TimeSpan.Zero);

    // Factory helpers
    private static OneDrivePhoto IphonePhoto(DateTimeOffset? ts, string name = "test_iOS.heic") =>
        new(Path: $@"C:\fake\{name}", FileName: name, Timestamp: ts,
            IsIphone: true, TimestampSource: ts.HasValue ? "filename" : null);

    private static OneDrivePhoto NonIphonePhoto(DateTimeOffset? ts = null, string name = "DSC001.jpg") =>
        new(Path: $@"C:\fake\{name}", FileName: name, Timestamp: ts,
            IsIphone: false, TimestampSource: null);

    private static HashSet<DateTimeOffset> GoogleSet(params DateTimeOffset[] timestamps) =>
        [.. timestamps];

    // -----------------------------------------------------------------------
    // Exact match
    // -----------------------------------------------------------------------

    [Fact]
    public void Match_ExactTimestamp_FileIsMatched()
    {
        var photo = IphonePhoto(Base);
        var result = PhotoMatcher.Match(GoogleSet(Base), [photo]);

        Assert.Single(result.Matched);
        Assert.Empty(result.Unmatched);
    }

    // -----------------------------------------------------------------------
    // Tolerance boundary (default ±2 seconds)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Match_WithinOrAtTolerance_FileIsMatched(int offsetSeconds)
    {
        var photo = IphonePhoto(Base);
        var google = GoogleSet(Base.AddSeconds(offsetSeconds));
        var result = PhotoMatcher.Match(google, [photo]);

        Assert.Single(result.Matched);
        Assert.Empty(result.Unmatched);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    public void Match_WithinNegativeTolerance_FileIsMatched(int offsetSeconds)
    {
        var photo = IphonePhoto(Base);
        var google = GoogleSet(Base.AddSeconds(offsetSeconds));
        var result = PhotoMatcher.Match(google, [photo]);

        Assert.Single(result.Matched);
        Assert.Empty(result.Unmatched);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-3)]
    [InlineData(60)]
    public void Match_OutsideTolerance_FileIsUnmatched(int offsetSeconds)
    {
        var photo = IphonePhoto(Base);
        var google = GoogleSet(Base.AddSeconds(offsetSeconds));
        var result = PhotoMatcher.Match(google, [photo]);

        Assert.Empty(result.Matched);
        Assert.Single(result.Unmatched);
    }

    // -----------------------------------------------------------------------
    // Custom tolerance
    // -----------------------------------------------------------------------

    [Fact]
    public void Match_CustomTolerance_RespectsOverride()
    {
        var photo = IphonePhoto(Base);
        var google = GoogleSet(Base.AddSeconds(5));

        // Default tolerance=2 → unmatched
        var r2 = PhotoMatcher.Match(google, [photo], toleranceSeconds: 2);
        Assert.Single(r2.Unmatched);

        // Expanded tolerance=10 → matched
        var r10 = PhotoMatcher.Match(google, [photo], toleranceSeconds: 10);
        Assert.Single(r10.Matched);
    }

    // -----------------------------------------------------------------------
    // Empty Google set
    // -----------------------------------------------------------------------

    [Fact]
    public void Match_EmptyGoogleSet_AllIphoneFilesAreUnmatched()
    {
        var photos = new List<OneDrivePhoto>
        {
            IphonePhoto(Base, "a_iOS.heic"),
            IphonePhoto(Base.AddSeconds(30), "b_iOS.heic"),
        };

        var result = PhotoMatcher.Match(GoogleSet(), photos);

        Assert.Empty(result.Matched);
        Assert.Equal(2, result.Unmatched.Count);
    }

    // -----------------------------------------------------------------------
    // Non-iPhone files are always skipped
    // -----------------------------------------------------------------------

    [Fact]
    public void Match_NonIphoneFile_IsSkippedEvenIfTimestampMatchesGoogle()
    {
        var photo = NonIphonePhoto(Base, "DSC001.jpg");
        var result = PhotoMatcher.Match(GoogleSet(Base), [photo]);

        Assert.Single(result.Skipped);
        Assert.Empty(result.Matched);
        Assert.Empty(result.Unmatched);
    }

    // -----------------------------------------------------------------------
    // iPhone file with no parseable timestamp is also skipped
    // -----------------------------------------------------------------------

    [Fact]
    public void Match_IphoneFileWithNoTimestamp_IsSkipped()
    {
        var photo = IphonePhoto(null, "notime_iOS.heic");
        var result = PhotoMatcher.Match(GoogleSet(Base), [photo]);

        Assert.Single(result.Skipped);
        Assert.Empty(result.Matched);
        Assert.Empty(result.Unmatched);
    }

    // -----------------------------------------------------------------------
    // Mixed batch
    // -----------------------------------------------------------------------

    [Fact]
    public void Match_MixedBatch_CorrectlyCategorisesAllFiles()
    {
        var matchedPhoto    = IphonePhoto(Base, "matched_iOS.heic");
        var unmatchedPhoto  = IphonePhoto(Base.AddHours(1), "unmatched_iOS.heic");
        var nonIphonePhoto  = NonIphonePhoto(Base, "camera.jpg");
        var noTimestampPhoto = IphonePhoto(null, "notime_iOS.heic");

        var photos = new List<OneDrivePhoto>
            { matchedPhoto, unmatchedPhoto, nonIphonePhoto, noTimestampPhoto };

        var result = PhotoMatcher.Match(GoogleSet(Base), photos);

        Assert.Equal([matchedPhoto],   result.Matched);
        Assert.Equal([unmatchedPhoto], result.Unmatched);
        Assert.Contains(nonIphonePhoto,  result.Skipped);
        Assert.Contains(noTimestampPhoto, result.Skipped);
    }

    // -----------------------------------------------------------------------
    // Integration: use real scan results + synthetic Google set
    // -----------------------------------------------------------------------

    [Fact]
    public void Match_RealScan_OneGoogleTimestamp_KeepsExactlyOneFile()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var photos = OneDriveScanner.ScanFolder(testDataDir);

        // Both test files share the same capture time. Google Photos has a
        // single photo at that time, so matching is one-to-one: exactly one
        // OneDrive file is kept, the other becomes a removal candidate.
        var knownUtc = new DateTimeOffset(2026, 4, 11, 7, 33, 33, 0, TimeSpan.Zero);
        var result = PhotoMatcher.Match(GoogleSet(knownUtc), photos);

        Assert.Single(result.Matched);
        Assert.Single(result.Unmatched);
    }

    // -----------------------------------------------------------------------
    // One-to-one matching (bursts)
    // -----------------------------------------------------------------------

    [Fact]
    public void Match_BurstKeptOneInGoogle_OnlyNearestFileMatched()
    {
        // Two burst frames ~1s apart; Google kept only one photo (at Base).
        var a = IphonePhoto(Base, "a_iOS.heic");
        var b = IphonePhoto(Base.AddSeconds(1), "b_iOS.heic");

        var result = PhotoMatcher.Match(GoogleSet(Base), [a, b]);

        Assert.Single(result.Matched);
        Assert.Single(result.Unmatched);
        // The frame nearest the Google timestamp is the one kept.
        Assert.Equal(Base, result.Matched[0].Timestamp);
    }

    [Fact]
    public void Match_BurstKeptTwoInGoogle_BothFramesMatched()
    {
        var a = IphonePhoto(Base, "a_iOS.heic");
        var b = IphonePhoto(Base.AddSeconds(1), "b_iOS.heic");
        // Google kept both frames (two distinct timestamps).
        var google = new List<DateTimeOffset> { Base, Base.AddSeconds(1) };

        var result = PhotoMatcher.Match(google, [a, b]);

        Assert.Equal(2, result.Matched.Count);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void Match_DuplicateGoogleTimestamps_KeepMultipleFiles()
    {
        // Two OneDrive frames at the identical time; Google kept both (two
        // entries with the same timestamp). A set would collapse these to one
        // and wrongly flag a file — a list preserves the count.
        var a = IphonePhoto(Base, "a_iOS.heic");
        var b = IphonePhoto(Base, "b_iOS.heic");
        var google = new List<DateTimeOffset> { Base, Base };

        var result = PhotoMatcher.Match(google, [a, b]);

        Assert.Equal(2, result.Matched.Count);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void Match_RealScanWithEmptyGoogleSet_BothFilesUnmatched()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var photos = OneDriveScanner.ScanFolder(testDataDir);

        var result = PhotoMatcher.Match(GoogleSet(), photos);

        Assert.Empty(result.Matched);
        Assert.Equal(2, result.Unmatched.Count);
    }
}
