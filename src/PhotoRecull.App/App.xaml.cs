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
        // A run cut off last time (crash, power loss) is finished from what it recorded.
        int interruptedRuns = AppServices.Database.FinishInterruptedRuns();
        MainWindow = new MainWindow();
        MainWindow.ShowStartPage(interruptedRuns > 0
            ? "A stage or delete was interrupted last time. History shows what was done; open its cleanup to finish the rest."
            : null);
        MainWindow.Activate();
    }

    // Called on a background thread when a second launch hands over to this one.
    public static void BringMainWindowToFront() =>
        MainWindow?.DispatcherQueue.TryEnqueue(() => MainWindow.BringToFront());

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
