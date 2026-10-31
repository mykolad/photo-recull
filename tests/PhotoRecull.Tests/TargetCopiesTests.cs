using PhotoRecull.Core;
using PhotoRecull.Core.Library;

namespace PhotoRecull.Tests;

public class TargetCopiesTests
{
    private sealed record Copy(string Target, PhotoFile Photo);

    private static readonly DateTimeOffset Noon = new(2026, 10, 29, 12, 0, 0, 208, TimeSpan.Zero);

    private static Copy CopyOn(string target, string fileName, DateTimeOffset taken, bool inWholeSeconds = false) =>
        new(target, new PhotoFile($@"C:\{target}\{fileName}", fileName, taken, IsIphone: true, TimestampSource: "exif")
        {
            TimestampInWholeSeconds = inWholeSeconds,
        });

    private static List<List<string>> Grouped(params Copy[] copies) =>
        TargetCopies.Group(copies, c => c.Photo, c => c.Target)
            .Select(group => group.Select(c => $"{c.Target}/{c.Photo.FileName}").ToList())
            .ToList();

    [Fact]
    public void ThePhotoOnTwoTargets_IsOnePhotoWithTwoCopies()
    {
        Assert.Equal(
            [["OneDrive/20261029_120000208_iOS.heic", "Synology/IMG_4719.HEIC"]],
            Grouped(CopyOn("OneDrive", "20261029_120000208_iOS.heic", Noon), CopyOn("Synology", "IMG_4719.HEIC", Noon)));
    }

    [Fact]
    public void ABurstOnTwoTargets_PairsFrameByFrame()
    {
        var frames = Enumerable.Range(0, 3).Select(i => Noon.AddMilliseconds(100 * i)).ToList();

        var groups = Grouped([
            .. frames.Select((t, i) => CopyOn("OneDrive", $"od{i}.heic", t)),
            .. frames.Select((t, i) => CopyOn("Synology", $"IMG_{i}.HEIC", t)),
        ]);

        Assert.Equal([["OneDrive/od0.heic", "Synology/IMG_0.HEIC"], ["OneDrive/od1.heic", "Synology/IMG_1.HEIC"], ["OneDrive/od2.heic", "Synology/IMG_2.HEIC"]], groups);
    }

    [Fact]
    public void AFrameWhoseTwinIsMissing_IsNotTakenForTheNextFrame()
    {
        Assert.Equal(
            [["OneDrive/od0.heic"], ["Synology/IMG_1.HEIC"]],
            Grouped(CopyOn("OneDrive", "od0.heic", Noon), CopyOn("Synology", "IMG_1.HEIC", Noon.AddMilliseconds(100))));
    }

    [Fact]
    public void PhotosOnTheSameTarget_StayApart_HoweverClose()
    {
        Assert.Equal(
            [["OneDrive/a.heic"], ["OneDrive/b.heic"]],
            Grouped(CopyOn("OneDrive", "a.heic", Noon), CopyOn("OneDrive", "b.heic", Noon)));
    }

    [Fact]
    public void AVideoTimedInWholeSeconds_PairsWithItsCopyTimedToTheMillisecond()
    {
        var wholeSecond = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            [["OneDrive/20261029_120000640_iOS.mov", "Synology/IMG_4721.MOV"]],
            Grouped(CopyOn("OneDrive", "20261029_120000640_iOS.mov", wholeSecond.AddMilliseconds(640)), CopyOn("Synology", "IMG_4721.MOV", wholeSecond, inWholeSeconds: true)));
    }

    [Fact]
    public void AWholeSecondTime_OnlyMatchesACaptureWithinItsOwnSecond()
    {
        var second = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        var groups = Grouped(
            CopyOn("OneDrive", "late.heic", second.AddMilliseconds(990)),
            CopyOn("OneDrive", "next.heic", second.AddMilliseconds(1200)),
            CopyOn("Synology", "IMG_1.HEIC", second, inWholeSeconds: true),
            CopyOn("Synology", "IMG_2.HEIC", second.AddSeconds(1), inWholeSeconds: true));

        Assert.Equal([["OneDrive/late.heic", "Synology/IMG_1.HEIC"], ["OneDrive/next.heic", "Synology/IMG_2.HEIC"]], groups);
    }

    [Fact]
    public void AWholeSecondCopy_WithTwoCapturesInItsSecond_IsLeftOnItsOwn()
    {
        var second = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        var groups = Grouped(
            CopyOn("OneDrive", "a.heic", second.AddMilliseconds(100)),
            CopyOn("OneDrive", "b.heic", second.AddMilliseconds(600)),
            CopyOn("Synology", "IMG_1.HEIC", second, inWholeSeconds: true));

        Assert.Equal([["OneDrive/a.heic"], ["OneDrive/b.heic"], ["Synology/IMG_1.HEIC"]], groups);
    }

    [Fact]
    public void AFrameTakenOnTheExactSecond_IsStillTimedToTheMillisecond_SoItIsNotTakenForTheNextFrame()
    {
        var onTheSecond = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            [["OneDrive/20261029_120000000_iOS.heic"], ["Synology/IMG_1.HEIC"]],
            Grouped(CopyOn("OneDrive", "20261029_120000000_iOS.heic", onTheSecond), CopyOn("Synology", "IMG_1.HEIC", onTheSecond.AddMilliseconds(100))));
    }

    [Fact]
    public void APhotoAndAVideoAtTheSameMoment_AreDifferentPhotos()
    {
        Assert.Equal(
            [["OneDrive/a.heic"], ["Synology/b.mov"]],
            Grouped(CopyOn("OneDrive", "a.heic", Noon), CopyOn("Synology", "b.mov", Noon)));
    }

    [Fact]
    public void PhotosWithoutACaptureTime_AreNeverGrouped()
    {
        var undated = new Copy("Synology", new PhotoFile(@"C:\s\x.png", "x.png", null, IsIphone: false, TimestampSource: null));
        var alsoUndated = new Copy("OneDrive", new PhotoFile(@"C:\o\x.png", "x.png", null, IsIphone: false, TimestampSource: null));

        Assert.Equal([["Synology/x.png"], ["OneDrive/x.png"]], Grouped(undated, alsoUndated));
    }
}
