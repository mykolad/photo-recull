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
        AppWindow.Closing += OnClosing;

#if DEBUG
        DebugScreenshot.CaptureIfRequested(this, RootGrid);
#endif
    }

    public IntPtr Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private bool _isCloseConfirmed;

    // Closing mid-run is the usual way a run gets cut off; the run log covers the
    // rest (crashes, power loss), but asking first avoids most of it.
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!AppServices.IsRunInProgress || _isCloseConfirmed)
            return;
        args.Cancel = true;
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Photos are still being moved or deleted",
            Content = "Closing now stops partway. What's done so far is kept in History, and the rest can be finished later. Close anyway?",
            PrimaryButtonText = "Close anyway",
            CloseButtonText = "Keep going",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };
        try
        {
            if (await dialog.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Another dialog (a confirmation) is already open; it has to be answered first.
            return;
        }
        _isCloseConfirmed = true;
        Close();
    }

    public void ShowStartPage(string? toastMessage = null)
    {
        if (AppServices.Database.LoadSettings().SetupCompletedAt is null)
            RootFrame.Navigate(typeof(SetupPage));
        else
            ShowShell(toastMessage);
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
        double scale = GetDpiForWindow(Handle) / 96.0;
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
