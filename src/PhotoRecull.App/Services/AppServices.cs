using PhotoRecull.Core.Storage;

namespace PhotoRecull.App.Services;

internal static class AppServices
{
    public static AppStateDatabase Database { get; } = new(AppDataPaths.DatabaseFile);

    public static GoogleAccount Google { get; } = new(Database);
}
