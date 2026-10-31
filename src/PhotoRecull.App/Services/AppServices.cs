using PhotoRecull.Core.Storage;

namespace PhotoRecull.App.Services;

internal static class AppServices
{
    public static AppStateDatabase Database { get; } = new(AppDataPaths.DatabaseFile);

    public static GoogleAccount Google { get; } = new(Database);

    // Set while a stage or delete is moving files, so closing the window asks first.
    public static bool IsRunInProgress { get; set; }
}
