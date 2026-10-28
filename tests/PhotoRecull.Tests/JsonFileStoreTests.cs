using PhotoRecull.Core;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.Tests;

public class JsonFileStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("photorecull-store-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void LoadOrCreateDefault_MissingFile_ReturnsDefault()
    {
        var settings = new JsonFileStore<AppSettings>(Path.Combine(_folder, "settings.json")).LoadOrCreateDefault();

        Assert.Empty(settings.Targets);
        Assert.Null(settings.SetupCompletedAt);
    }

    [Fact]
    public void LoadOrCreateDefault_DamagedFile_KeepsItAsideAndReturnsDefault()
    {
        var path = Path.Combine(_folder, "library.json");
        File.WriteAllText(path, "{ \"Cleanups\": [ { not json");

        var library = new JsonFileStore<LibraryData>(path).LoadOrCreateDefault();

        Assert.Empty(library.Cleanups);
        Assert.False(File.Exists(path));
        var keptAside = Assert.Single(Directory.GetFiles(_folder, "library.json.damaged-*"));
        Assert.Equal("{ \"Cleanups\": [ { not json", File.ReadAllText(keptAside));
    }

    [Fact]
    public void Save_CreatesFolderAndRoundTripsSettings()
    {
        var store = new JsonFileStore<AppSettings>(Path.Combine(_folder, "nested", "settings.json"));
        var saved = new AppSettings
        {
            SignInBrowser = "chrome",
            Targets = [new TargetSettings(TargetKind.OneDrive, @"C:\Users\me\OneDrive\Pictures\Camera Roll")],
            SetupCompletedAt = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero),
        };

        store.Save(saved);
        var loaded = store.LoadOrCreateDefault();

        Assert.Equal(saved.SignInBrowser, loaded.SignInBrowser);
        Assert.Equal(saved.Targets, loaded.Targets);
        Assert.Equal(saved.SetupCompletedAt, loaded.SetupCompletedAt);
        Assert.Contains("\"OneDrive\"", File.ReadAllText(store.FilePath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
    }

    [Fact]
    public void Save_OverwritesAndRoundTripsLibrary()
    {
        var store = new JsonFileStore<LibraryData>(Path.Combine(_folder, "library.json"));
        var cleanup = new Cleanup(Guid.NewGuid(), new(2026, 7, 6), new(2026, 7, 31), CleanupState.Staged,
            PickedCount: 129, PickedAt: DateTimeOffset.UtcNow, LastActivityAt: DateTimeOffset.UtcNow)
        {
            ToRemoveCount = 41,
            StagedCount = 40,
            KeptByUserCount = 1,
        };

        store.Save(new LibraryData());
        store.Save(new LibraryData { Cleanups = [cleanup] });

        Assert.Equal(cleanup, Assert.Single(store.LoadOrCreateDefault().Cleanups));
    }

    [Fact]
    public void Save_ConcurrentSavesToTheSameFile_AllSucceedAndLeaveNoTemporaryFiles()
    {
        var store = new JsonFileStore<AppSettings>(Path.Combine(_folder, "settings.json"));

        Parallel.For(0, 20, i => store.Save(new AppSettings { SignInBrowser = $"browser-{i}" }));

        Assert.StartsWith("browser-", store.LoadOrCreateDefault().SignInBrowser);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }
}
