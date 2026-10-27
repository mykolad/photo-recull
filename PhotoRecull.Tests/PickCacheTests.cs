using PhotoRecull;
using Xunit;

namespace PhotoRecull.Tests;

public class PickCacheTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = Path.GetTempFileName();
        try
        {
            var data = new PickCacheData(
                CapturedAtUtc: new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero),
                From: new DateOnly(2026, 8, 1),
                To: new DateOnly(2026, 8, 31),
                Timestamps:
                [
                    new DateTimeOffset(2026, 8, 1, 13, 15, 58, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 1, 13, 15, 59, TimeSpan.Zero),
                ]);

            PickCache.Save(path, data);
            var loaded = PickCache.Load(path);

            Assert.Equal(data.CapturedAtUtc, loaded.CapturedAtUtc);
            Assert.Equal(data.From, loaded.From);
            Assert.Equal(data.To, loaded.To);
            Assert.Equal(data.Timestamps, loaded.Timestamps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveThenLoad_PreservesDuplicateTimestamps()
    {
        var path = Path.GetTempFileName();
        try
        {
            var ts = new DateTimeOffset(2026, 8, 1, 13, 15, 58, TimeSpan.Zero);
            var data = new PickCacheData(DateTimeOffset.UnixEpoch, null, null, [ts, ts]);

            PickCache.Save(path, data);
            var loaded = PickCache.Load(path);

            Assert.Equal(2, loaded.Timestamps.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
