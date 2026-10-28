namespace PhotoRecull.App.Services;

internal static class AppDataPaths
{
    // Overridable so the app can be run against a throwaway data folder, e.g. to
    // try the first-run Setup again or to check screens against sample data.
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("PHOTORECULL_DATA_FOLDER")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoRecull");

    public static string DatabaseFile => Path.Combine(Root, "photorecull.db");
}
