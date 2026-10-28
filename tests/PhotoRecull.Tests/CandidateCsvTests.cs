using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class CandidateCsvTests
{
    [Fact]
    public void WriteNewestFirst_OrdersByTimestampAndEscapesFields()
    {
        var older = new PhotoFile(@"C:\Camera Roll\old.heic", "old.heic",
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero), IsIphone: true, TimestampSource: "exif");
        var newer = new PhotoFile("C:\\Camera Roll\\new, \"quoted\".heic", "new, \"quoted\".heic",
            new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero), IsIphone: true, TimestampSource: "filename");
        var path = Path.GetTempFileName();
        try
        {
            CandidateCsv.WriteNewestFirst(path, [older, newer]);

            Assert.Equal(
            [
                "TimestampUTC,TimestampSource,FileName,FullPath",
                "2026-08-02 10:00:00,filename,\"new, \"\"quoted\"\".heic\",\"C:\\Camera Roll\\new, \"\"quoted\"\".heic\"",
                "2026-08-01 10:00:00,exif,old.heic,C:\\Camera Roll\\old.heic",
            ], File.ReadAllLines(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
