using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using Windows.Graphics;

namespace PhotoRecull.App;

public sealed partial class MainWindow : Window
{
    private const int DesignWidth = 1280;
    private const int DesignHeight = 800;
    private const int MinimumWidth = 960;
    private const int MinimumHeight = 600;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        SizeToDesignSize();

#if DEBUG
        DebugScreenshot.CaptureIfRequested(this, RootGrid);
#endif
    }

    public void ShowStartPage()
    {
        if (AppServices.Database.LoadSettings().SetupCompletedAt is null)
            RootFrame.Navigate(typeof(SetupPage));
        else
            ShowShell();
    }

    public void ShowShell(string? toastMessage = null) =>
        RootFrame.Navigate(typeof(ShellPage), toastMessage);

    public void ShowRangeInTitle(string? rangeLabel)
    {
        Title = rangeLabel is null ? "PhotoRecull" : $"PhotoRecull — {rangeLabel}";
        TitleText.Text = Title;
    }

    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
    }

    private void SizeToDesignSize()
    {
        // AppWindow sizes are in physical pixels, so scale the design size by the
        // monitor's DPI to get the intended 1280×800 at any display scaling.
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(DesignWidth * scale), (int)(DesignHeight * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
            presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
