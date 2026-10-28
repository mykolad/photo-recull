using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoRecull.App.Services;
using PhotoRecull.App.ViewModels;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.Views;

public sealed record ConnectedServiceRow(string Glyph, string Name, string Detail);

public sealed partial class ShellPage : Page
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(5);
    private CancellationTokenSource? _toastTimer;

    public ShellPage()
    {
        InitializeComponent();
        Current = this;
        Navigation.SelectedItem = LibraryItem;
        RefreshConnectedServices();
    }

    public static ShellPage? Current { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string toastMessage)
            ShowToast(toastMessage);
#if DEBUG
        if (DebugScreenshot.RequestedCleanupStep() is { } debugRequest)
            OpenCleanupFlow(new CleanupFlowRequest(debugRequest.CleanupId,
                AppServices.Database.LoadSettings().Targets));
#endif
    }

    public async void ShowToast(string message)
    {
        _toastTimer?.Cancel();
        var timer = _toastTimer = new CancellationTokenSource();
        Toast.Message = message;
        Toast.IsOpen = true;
        try
        {
            await Task.Delay(ToastDuration, timer.Token);
            Toast.IsOpen = false;
        }
        catch (TaskCanceledException)
        {
        }
    }

    public void RefreshConnectedServices()
    {
        var settings = AppServices.Database.LoadSettings();
        var rows = new List<ConnectedServiceRow>
        {
            new("", "Google Photos", AppServices.Google.IsSignedIn ? "Source · Signed in" : "Source · Not signed in"),
        };
        rows.AddRange(settings.Targets.Select(t =>
            new ConnectedServiceRow(TargetGlyph(t.Kind), SyncTargets.DisplayName(t.Kind), $"Target · {t.FolderPath}")));
        ConnectedServicesList.ItemsSource = rows;
    }

    public static string TargetGlyph(TargetKind kind) => kind == TargetKind.OneDrive ? "" : "";

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItem as NavigationViewItem)?.Tag switch
            {
                "History" => typeof(HistoryPage),
                _ => typeof(LibraryPage),
            };
        // An open cleanup belongs under Library, so Library becoming selected
        // (e.g. when the view first loads) mustn't close it; clicking Library to
        // leave the cleanup is handled in OnNavigationItemInvoked.
        bool isCleanupOpenUnderLibrary = page == typeof(LibraryPage)
            && ContentFrame.CurrentSourcePageType == typeof(CleanupFlowPage);
        if (ContentFrame.CurrentSourcePageType != page && !isCleanupOpenUnderLibrary)
            ContentFrame.Navigate(page);
    }

    // Selecting Library while a cleanup is open doesn't change the selection, so
    // SelectionChanged doesn't fire; handle the click itself.
    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer == LibraryItem && ContentFrame.CurrentSourcePageType != typeof(LibraryPage))
            ShowLibrary();
    }

    public void ShowLibrary()
    {
        Navigation.SelectedItem = LibraryItem;
        ContentFrame.Navigate(typeof(LibraryPage));
        Navigation.IsPaneOpen = true;
    }

    public void SetNavigationLocked(bool isLocked)
    {
        LibraryItem.IsEnabled = HistoryItem.IsEnabled = ManageTargetsButton.IsEnabled = !isLocked;
        if (Navigation.SettingsItem is NavigationViewItem settingsItem)
            settingsItem.IsEnabled = !isLocked;
    }

    public void ShowHistory()
    {
        Navigation.IsPaneOpen = true;
        Navigation.SelectedItem = HistoryItem;
    }

    // WinUI allows one open ContentDialog; a quick second click on New cleanup
    // would otherwise try to open another one and throw.
    private bool _isNewCleanupDialogOpen;

    public async Task StartNewCleanupAsync()
    {
        if (_isNewCleanupDialogOpen)
            return;
        _isNewCleanupDialogOpen = true;
        try
        {
            var dialog = new NewCleanupDialog { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                OpenCleanupFlow(new CleanupFlowRequest(ExistingCleanupId: null, dialog.SelectedTargets));
        }
        finally
        {
            _isNewCleanupDialogOpen = false;
        }
    }

    public void OpenCleanupFlow(CleanupFlowRequest request)
    {
        Navigation.SelectedItem = LibraryItem;
        ContentFrame.Navigate(typeof(CleanupFlowPage), request);
    }

    public void CollapseNavigationPane() => Navigation.IsPaneOpen = false;

    private void OnManageTargetsClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        Navigation.SelectedItem = Navigation.SettingsItem;

    private void OnPaneOpening(NavigationView sender, object args) =>
        ConnectedPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

    private void OnPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) =>
        ConnectedPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
}
