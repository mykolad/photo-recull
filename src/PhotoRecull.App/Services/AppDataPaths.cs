namespace PhotoRecull.App.Services;

internal static class AppDataPaths
{
    // Overridable so the app can be run against a throwaway data folder, e.g. to
    // try the first-run Setup again or to check screens against sample data.
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("PHOTORECULL_DATA_FOLDER")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoRecull");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string LibraryFile => Path.Combine(Root, "library.json");

    public static string GoogleTokenFolder => Path.Combine(Root, "google-token");

    public static string PicksFolder => Path.Combine(Root, "picks");
}
