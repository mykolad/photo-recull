using Microsoft.UI.Xaml;
using PhotoRecull.App.Services;

namespace PhotoRecull.App;

public partial class App : Application
{
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => AppendToCrashLog(e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.ShowStartPage();
        MainWindow.Activate();
    }

    private static void AppendToCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppDataPaths.Root);
            File.AppendAllText(Path.Combine(AppDataPaths.Root, "crash.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
