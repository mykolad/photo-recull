using System.Text;
using PhotoRecull.Core;

namespace PhotoRecull.Tests;

// Photos from cameras other than iPhones: read with their camera and capture
// time, skipped unless a target takes every camera, and never removed on the
// strength of a guessed time zone.
public class OtherCamerasTests : IDisposable
{
    private static readonly TimeSpan GuessedOffset = TimeSpan.FromHours(2);

    // A Pixel photo saved at 11:23:05 local time, without its time zone.
    private static readonly DateTimeOffset ClockTime = new(2026, 10, 25, 11, 23, 5, GuessedOffset);

    private readonly string _folder = Directory.CreateTempSubdirectory("photorecull-cameras-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static PhotoFile OtherCameraPhoto(DateTimeOffset timestamp, bool offsetGuessed, string name = "PXL_1.jpg") =>
        new(Path: $@"C:\fake\{name}", FileName: name, Timestamp: timestamp, IsIphone: false, TimestampSource: "exif")
        {
            TimestampOffsetGuessed = offsetGuessed,
            Camera = "Google Pixel 8",
        };

    [Fact]
    public void Match_SkipsOtherCameras_UnlessTheTargetTakesThemAll()
    {
        var photo = OtherCameraPhoto(ClockTime, offsetGuessed: false);

        Assert.Equal([photo], PhotoMatcher.Match([ClockTime], [photo]).Skipped);
        Assert.Equal([photo], PhotoMatcher.Match([ClockTime], [photo], onlyIphonePhotos: false).Matched);
    }

    [Fact]
    public void Match_ABurstWithoutATimeZone_StaysWhole_WhenOneFrameWasKept()
    {
        // Without a time zone, which frame was kept can't be told for sure.
        var frames = Enumerable.Range(0, 3)
            .Select(i => OtherCameraPhoto(ClockTime.AddMilliseconds(400 * i), offsetGuessed: true, $"PXL_{i}.jpg"))
            .ToList();

        var result = PhotoMatcher.Match([frames[1].Timestamp!.Value], frames, onlyIphonePhotos: false);

        Assert.Equal(frames, result.Matched);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void Match_TwoCamerasPhotosOfTheSameClockTimeWithoutTimeZones_BothStay()
    {
        var picked = OtherCameraPhoto(ClockTime, offsetGuessed: true, "PXL_1.jpg");
        var notPicked = OtherCameraPhoto(ClockTime, offsetGuessed: true, "DSC_1.jpg") with { Camera = "Canon EOS R6" };

        var result = PhotoMatcher.Match([ClockTime], [notPicked, picked], onlyIphonePhotos: false);

        Assert.Equal(2, result.Matched.Count);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void Match_AWrongTimeZoneGuess_KeepsThePhoto_WhenAPickedPhotoCouldBeItElsewhere()
    {
        // Taken in New York (UTC-5): Google has it at 16:23:05 UTC, seven hours
        // from where the guess puts it.
        var photo = OtherCameraPhoto(ClockTime, offsetGuessed: true);
        var inNewYork = new DateTimeOffset(2026, 10, 25, 16, 23, 5, TimeSpan.Zero);

        Assert.Equal([photo], PhotoMatcher.Match([inNewYork], [photo], onlyIphonePhotos: false).Matched);
    }

    [Fact]
    public void Match_AWrongTimeZoneGuess_IsCheckedAgainstTheWholePick()
    {
        // Picked, but on a day outside the range once in UTC-5.
        var photo = OtherCameraPhoto(ClockTime, offsetGuessed: true);
        var inNewYork = new DateTimeOffset(2026, 10, 25, 16, 23, 5, TimeSpan.Zero);

        var result = PhotoMatcher.Match([], [photo], onlyIphonePhotos: false, timestampsAtAnyOffset: [inNewYork]);

        Assert.Equal([photo], result.Matched);
    }

    [Fact]
    public void Match_APhotoWithAGuessedTimeZone_NeverTakesTheMatchOfOneWithAKnownTimeZone_AndStaysToo()
    {
        // The picked photo may be either: the iPhone's keeps the match, and the
        // other camera's isn't taken for a culled frame just for being a second away.
        var iphone = new PhotoFile(@"C:\fake\IMG_1.HEIC", "IMG_1.HEIC", ClockTime.AddSeconds(1), IsIphone: true, TimestampSource: "exif");
        var guessed = OtherCameraPhoto(ClockTime, offsetGuessed: true);

        var result = PhotoMatcher.Match([ClockTime], [guessed, iphone], onlyIphonePhotos: false);

        Assert.Equal([iphone, guessed], result.Matched);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void Match_APhotoWithAGuessedTimeZone_StaysWhenAnotherCamerasPhotoTookItsMatch()
    {
        var pixel = OtherCameraPhoto(ClockTime, offsetGuessed: true, "PXL_1.jpg");
        var canon = OtherCameraPhoto(ClockTime.AddSeconds(1), offsetGuessed: true, "DSC_1.jpg") with { Camera = "Canon EOS R6" };

        var result = PhotoMatcher.Match([ClockTime.AddSeconds(1)], [pixel, canon], onlyIphonePhotos: false);

        Assert.Equal(2, result.Matched.Count);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void SetAsideGuessedPhotosNearTheEdges_KeepsAPhotoThatCouldBeFromTheRangeBefore()
    {
        // 00:30 on 1 August where it was taken could be 31 July anywhere east of UTC.
        var justAfterMidnight = OtherCameraPhoto(new DateTimeOffset(2026, 8, 1, 0, 30, 0, TimeSpan.Zero), offsetGuessed: true, "PXL_1.jpg");
        var midMonth = OtherCameraPhoto(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero), offsetGuessed: true, "PXL_2.jpg");
        var known = OtherCameraPhoto(new DateTimeOffset(2026, 8, 1, 0, 30, 0, TimeSpan.Zero), offsetGuessed: false, "PXL_3.jpg");
        var match = PhotoMatcher.Match([], [justAfterMidnight, midMonth, known], onlyIphonePhotos: false);

        var result = DateRangeFilter.SetAsideGuessedPhotosNearTheEdges(match, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.Equal([justAfterMidnight], result.Skipped);
        Assert.Equal([known, midMonth], result.Unmatched.OrderBy(p => p.Timestamp));
        Assert.Equal("No time zone, and it could be from just outside the range", PhotoMatcher.WhySkipped(justAfterMidnight, onlyIphonePhotos: false));
    }

    [Fact]
    public void Match_APhotoNoPickedPhotoCouldBe_IsACandidate()
    {
        var photo = OtherCameraPhoto(ClockTime, offsetGuessed: true);
        var twentyMinutesLater = ClockTime.AddMinutes(20).AddSeconds(30);

        Assert.Equal([photo], PhotoMatcher.Match([twentyMinutesLater], [photo], onlyIphonePhotos: false).Unmatched);
    }

    [Theory]
    [InlineData(@"C:\fake\PXL_1.jpg", true, "Not taken with an iPhone (Google Pixel 8)")]
    [InlineData(@"C:\fake\Screenshot.png", false, "No capture time")]
    [InlineData(@"C:\fake\VID_1.mp4", false, "No recording start time")]
    public void WhySkipped_SaysWhatPhotoRecullCouldntUse(string path, bool hasTime, string expected)
    {
        var photo = new PhotoFile(path, Path.GetFileName(path), hasTime ? ClockTime : null, IsIphone: false, TimestampSource: null)
        {
            Camera = "Google Pixel 8",
        };

        Assert.Equal(expected, PhotoMatcher.WhySkipped(photo));
    }

    [Fact]
    public void Scan_ReadsAnotherCamerasPhoto_WithItsCameraAndTimeZone()
    {
        WriteExifJpeg("PXL_1.jpg", "Google", "Pixel 8", "2026:10:25 11:23:05", subSecond: "120", offset: "-05:00");

        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));

        Assert.False(photo.IsIphone);
        Assert.Equal("Google Pixel 8", photo.Camera);
        Assert.False(photo.TimestampOffsetGuessed);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 16, 23, 5, 120, TimeSpan.Zero), photo.Timestamp);
    }

    [Fact]
    public void Scan_APhotoSavedWithoutItsTimeZone_GuessesThePcsTimeZone()
    {
        WriteExifJpeg("DSC_1.jpg", "Canon", "Canon EOS R6", "2026:10:25 11:23:05", subSecond: null, offset: null);

        var photo = Assert.Single(PhotoScanner.ScanFolder(_folder));

        var clockTime = new DateTime(2026, 10, 25, 11, 23, 5);
        Assert.Equal("Canon EOS R6", photo.Camera);
        Assert.True(photo.TimestampOffsetGuessed);
        Assert.Equal(new DateTimeOffset(clockTime, TimeZoneInfo.Local.GetUtcOffset(clockTime)), photo.Timestamp);
        Assert.Equal(clockTime, photo.Timestamp!.Value.DateTime);
    }

    // A JPEG that's only its EXIF: camera make and model in IFD0, capture time in
    // the Exif IFD, little-endian, with no image data, which is all the scanner reads.
    private void WriteExifJpeg(string fileName, string make, string model, string taken, string? subSecond, string? offset)
    {
        List<(ushort Tag, byte[] Value)> exif = [(0x9003, Ascii(taken))];
        if (offset is not null)
            exif.Add((0x9011, Ascii(offset)));
        if (subSecond is not null)
            exif.Add((0x9291, Ascii(subSecond)));
        List<(ushort Tag, byte[] Value)> ifd0 = [(0x010F, Ascii(make)), (0x0110, Ascii(model))];

        const int Ifd0Start = 8;
        int exifStart = Ifd0Start + Ifd(ifd0, Ifd0Start, exifPointer: 0).Length;
        byte[] tiff = [(byte)'I', (byte)'I', 42, 0, .. BitConverter.GetBytes(Ifd0Start),
            .. Ifd(ifd0, Ifd0Start, exifStart), .. Ifd(exif, exifStart, exifPointer: null)];

        byte[] app1 = [.. "Exif\0\0"u8, .. tiff];
        var length = app1.Length + 2;
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. app1, 0xFF, 0xD9];
        File.WriteAllBytes(Path.Combine(_folder, fileName), jpeg);
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value + "\0");

    private static byte[] Ifd(List<(ushort Tag, byte[] Value)> asciiEntries, int start, int? exifPointer)
    {
        int count = asciiEntries.Count + (exifPointer is null ? 0 : 1);
        int dataStart = start + 2 + (count * 12) + 4;
        var entries = new List<byte>();
        var data = new List<byte>();
        foreach (var (tag, value) in asciiEntries.OrderBy(e => e.Tag))
        {
            entries.AddRange([.. BitConverter.GetBytes(tag), .. BitConverter.GetBytes((ushort)2), .. BitConverter.GetBytes(value.Length)]);
            if (value.Length <= 4)
            {
                entries.AddRange(value.Concat(new byte[4 - value.Length]));
                continue;
            }
            entries.AddRange(BitConverter.GetBytes(dataStart + data.Count));
            data.AddRange(value);
            if (data.Count % 2 == 1)
                data.Add(0);
        }
        if (exifPointer is { } pointer)
            entries.AddRange([.. BitConverter.GetBytes((ushort)0x8769), .. BitConverter.GetBytes((ushort)4), .. BitConverter.GetBytes(1), .. BitConverter.GetBytes(pointer)]);
        return [.. BitConverter.GetBytes((ushort)count), .. entries, .. BitConverter.GetBytes(0), .. data];
    }
}
