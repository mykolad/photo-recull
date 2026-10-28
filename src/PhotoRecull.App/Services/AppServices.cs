using PhotoRecull.Core;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.Services;

internal static class AppServices
{
    public static JsonFileStore<AppSettings> SettingsStore { get; } = new(AppDataPaths.SettingsFile);

    public static JsonFileStore<LibraryData> LibraryStore { get; } = new(AppDataPaths.LibraryFile);

    public static GoogleAccount Google { get; } = new();
}
