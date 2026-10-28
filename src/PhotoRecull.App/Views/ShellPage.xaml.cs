using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoRecull.App.Services;
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
        var settings = AppServices.SettingsStore.LoadOrCreateDefault();
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
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }

    private void OnManageTargetsClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        Navigation.SelectedItem = Navigation.SettingsItem;

    private void OnPaneOpening(NavigationView sender, object args) =>
        ConnectedPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

    private void OnPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) =>
        ConnectedPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
}
