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
    public void LoadOrCreateDefault_FirstLaunchWithoutTheDataFolder_ReturnsDefault()
    {
        var settings = new JsonFileStore<AppSettings>(Path.Combine(_folder, "not-created-yet", "settings.json"))
            .LoadOrCreateDefault();

        Assert.Null(settings.SetupCompletedAt);
    }

    [Fact]
    public void LoadOrCreateDefault_DamagedFile_BacksUpItsContentsAndReturnsDefault()
    {
        var path = Path.Combine(_folder, "library.json");
        File.WriteAllText(path, "{ \"Cleanups\": [ { not json");

        var library = new JsonFileStore<LibraryData>(path).LoadOrCreateDefault();

        Assert.Empty(library.Cleanups);
        var backup = Assert.Single(Directory.GetFiles(_folder, "library.json.damaged-*"));
        Assert.Equal("{ \"Cleanups\": [ { not json", File.ReadAllText(backup));
    }

    [Fact]
    public void LoadOrCreateDefault_DamagedFile_NeverRemovesTheFileSoANewerSaveIsKept()
    {
        var path = Path.Combine(_folder, "library.json");
        File.WriteAllText(path, "{ not json");
        var store = new JsonFileStore<LibraryData>(path);
        var cleanup = new Cleanup(Guid.NewGuid(), new(2026, 7, 6), new(2026, 7, 31), CleanupState.Picked,
            PickedCount: 1, PickedAt: DateTimeOffset.UtcNow, LastActivityAt: DateTimeOffset.UtcNow);

        store.LoadOrCreateDefault();
        store.Save(new LibraryData { Cleanups = [cleanup] });
        store.LoadOrCreateDefault();

        Assert.Equal(cleanup, Assert.Single(store.LoadOrCreateDefault().Cleanups));
    }

    [Fact]
    public void LoadOrCreateDefault_DamagedFileLoadedByManyAtOnce_NoneThrowAndOneBackupIsKept()
    {
        var path = Path.Combine(_folder, "library.json");
        File.WriteAllText(path, "{ \"Cleanups\": [ { not json");
        var store = new JsonFileStore<LibraryData>(path);

        Parallel.For(0, 20, _ => Assert.Empty(store.LoadOrCreateDefault().Cleanups));

        Assert.Single(Directory.GetFiles(_folder, "library.json.damaged-*"));
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
    public void Save_RoundTripsTheTargetsACleanupWasStartedFor()
    {
        var store = new JsonFileStore<LibraryData>(Path.Combine(_folder, "library.json"));
        var oneDrive = new TargetSettings(TargetKind.OneDrive, @"C:\Users\me\OneDrive\Pictures\Camera Roll");
        var cleanup = new Cleanup(Guid.NewGuid(), new(2026, 8, 1), new(2026, 8, 31), CleanupState.Picked,
            PickedCount: 10, PickedAt: DateTimeOffset.UtcNow, LastActivityAt: DateTimeOffset.UtcNow)
        {
            Targets = [oneDrive],
        };

        store.Save(new LibraryData { Cleanups = [cleanup] });

        Assert.Equal([oneDrive], Assert.Single(store.LoadOrCreateDefault().Cleanups).Targets!);
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
