using PhotoRecull.Core;
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

    [Fact]
    public void Save_FailingMidway_LeavesThePreviousPickIntact()
    {
        var folder = Directory.CreateTempSubdirectory("photorecull-pick-").FullName;
        var path = Path.Combine(folder, "pick.json");
        try
        {
            var previous = new PickCacheData(DateTimeOffset.UtcNow, null, null, [DateTimeOffset.UnixEpoch]);
            PickCache.Save(path, previous);

            // Locking the saved pick makes replacing it fail, as an interrupted save would.
            Exception? saveFailure;
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                saveFailure = Record.Exception(() =>
                    PickCache.Save(path, new PickCacheData(DateTimeOffset.UtcNow, null, null, [])));

            Assert.True(saveFailure is IOException or UnauthorizedAccessException);

            Assert.Equal(previous.Timestamps, PickCache.Load(path).Timestamps);
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
