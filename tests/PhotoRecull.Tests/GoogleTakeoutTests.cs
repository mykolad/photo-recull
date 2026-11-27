using System.IO.Compression;
using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class GoogleTakeoutTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("photorecull-takeout-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static readonly DateTimeOffset Taken = new(2026, 10, 25, 9, 23, 23, TimeSpan.Zero);

    private static string Sidecar(string title, DateTimeOffset taken, bool trashed = false) => $$"""
        {
          "title": "{{title}}",
          "description": "",
          "creationTime": { "timestamp": "{{taken.AddDays(1).ToUnixTimeSeconds()}}", "formatted": "" },
          "photoTakenTime": { "timestamp": "{{taken.ToUnixTimeSeconds()}}", "formatted": "" },
          "url": "https://photos.google.com/photo/example"{{(trashed ? ",\n  \"trashed\": true" : "")}}
        }
        """;

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Folder_ReadsEveryPhotosTakenTime_AsUtc()
    {
        Write("Takeout/Google Photos/Photos from 2026/IMG_4821.HEIC.json", Sidecar("IMG_4821.HEIC", Taken));
        Write("Takeout/Google Photos/Photos from 2026/IMG_4822.MOV.supplemental-metadata.json", Sidecar("IMG_4822.MOV", Taken.AddMinutes(5)));

        var read = GoogleTakeout.ReadCaptureTimes([_folder]);

        Assert.Equal([Taken, Taken.AddMinutes(5)], read.Timestamps.Order());
        Assert.Equal(2, read.SidecarsRead);
        Assert.Equal(TimeSpan.Zero, read.Timestamps[0].Offset);
    }

    [Fact]
    public void AlbumCopies_CountOnce_ButBurstFramesInTheSameSecondCountTwice()
    {
        Write("Takeout/Google Photos/Photos from 2026/IMG_4821.HEIC.json", Sidecar("IMG_4821.HEIC", Taken));
        Write("Takeout/Google Photos/Holiday/IMG_4821.HEIC.json", Sidecar("IMG_4821.HEIC", Taken));
        Write("Takeout/Google Photos/Photos from 2026/IMG_4823.HEIC.json", Sidecar("IMG_4823.HEIC", Taken));

        var read = GoogleTakeout.ReadCaptureTimes([_folder]);

        Assert.Equal([Taken, Taken], read.Timestamps);
    }

    [Fact]
    public void TrashedPhotos_AlbumMetadataAndUnreadableJson_AreLeftOut()
    {
        Write("Takeout/Google Photos/Photos from 2026/IMG_1.HEIC.json", Sidecar("IMG_1.HEIC", Taken));
        Write("Takeout/Google Photos/Trash/IMG_2.HEIC.json", Sidecar("IMG_2.HEIC", Taken.AddHours(1), trashed: true));
        Write("Takeout/Google Photos/Holiday/metadata.json", """{ "title": "Holiday", "date": { "timestamp": "1700000000" } }""");
        Write("Takeout/Google Photos/Photos from 2026/broken.json", "{ not json");
        Write("Takeout/Google Photos/Photos from 2026/IMG_3.HEIC.json", """{ "title": "IMG_3.HEIC", "photoTakenTime": { "timestamp": "0" } }""");

        var read = GoogleTakeout.ReadCaptureTimes([_folder]);

        Assert.Equal([Taken], read.Timestamps);
        Assert.Equal(2, read.SidecarsRead);
        Assert.Equal(1, read.Trashed);
    }

    [Fact]
    public void ZipFiles_AreReadWithoutExtracting_AndTogetherWithFolders()
    {
        var zipPath = Path.Combine(_folder, "takeout-20261127T100000Z-001.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("Takeout/Google Photos/Photos from 2026/IMG_1.HEIC.json").Open()))
                writer.Write(Sidecar("IMG_1.HEIC", Taken));
            zip.CreateEntry("Takeout/Google Photos/Photos from 2026/IMG_1.HEIC");
        }
        var extracted = Path.Combine(_folder, "extracted");
        Write("extracted/Takeout/Google Photos/Photos from 2026/IMG_2.HEIC.json", Sidecar("IMG_2.HEIC", Taken.AddDays(1)));

        var read = GoogleTakeout.ReadCaptureTimes([zipPath, extracted]);

        Assert.Equal([Taken, Taken.AddDays(1)], read.Timestamps.Order());
    }

    [Fact]
    public void APathThatIsNeitherAFolderNorAZip_Throws()
    {
        var tgz = Write("takeout-001.tgz", "");

        var error = Assert.Throws<FileNotFoundException>(() => GoogleTakeout.ReadCaptureTimes([tgz]));
        Assert.Contains(".tgz", error.Message);
    }

    [Fact]
    public void TakeoutTimes_MatchLikeAPick()
    {
        // The burst kept one frame in Google Photos: the matcher keeps that frame and
        // marks the other, as it does with a pick.
        Write("Takeout/Google Photos/Photos from 2026/IMG_4821.HEIC.json", Sidecar("IMG_4821.HEIC", Taken));
        var kept = new PhotoFile("20261025_092323544_iOS.heic", "20261025_092323544_iOS.heic", Taken.AddMilliseconds(544), IsIphone: true, "filename");
        var deleted = new PhotoFile("20261025_092324102_iOS.heic", "20261025_092324102_iOS.heic", Taken.AddSeconds(1).AddMilliseconds(102), IsIphone: true, "filename");

        var result = PhotoMatcher.Match(GoogleTakeout.ReadCaptureTimes([_folder]).Timestamps, [kept, deleted]);

        Assert.Equal([kept], result.Matched);
        Assert.Equal([deleted], result.Unmatched);
    }
}
