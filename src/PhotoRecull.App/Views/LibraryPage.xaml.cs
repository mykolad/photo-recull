using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoRecull.App.ViewModels;

namespace PhotoRecull.App.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryPage()
    {
        InitializeComponent();
        ShowCoverageForSelectedYear();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.CoverageYear))
                ShowCoverageForSelectedYear();
        };
        Coverage.SegmentClicked += (_, segment) => ViewModel.OpenCleanup(segment.CleanupId);
        ActualThemeChanged += (_, _) => RecreateCardsWithNewThemeBrushes();
        Loaded += async (_, _) => await ShowStagedCleanupsSettledAsync();
    }

    // Library is shown at once, then rebuilt if a cleanup's state changed meanwhile (a
    // Staged one turned out to be Done), unless the user has moved on to another page.
    private async Task ShowStagedCleanupsSettledAsync()
    {
        var (isOutdated, message) = await ViewModel.SettleStagedCleanupsAsync();
        if (!ReferenceEquals(Frame?.Content, this))
            return;
        if (isOutdated)
            ShellPage.Current?.RefreshLibrary(ViewModel.CoverageYear);
        if (message is not null)
            ShellPage.Current?.ShowToast(message);
    }

    public LibraryViewModel ViewModel { get; } = new();

    // A refreshed Library (see ShellPage.RefreshLibrary) shows the year it showed before.
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int coverageYear)
            ViewModel.ShowCoverageYear(coverageYear);
    }

    private void ShowCoverageForSelectedYear() =>
        Coverage.Show(ViewModel.CoverageYear, ViewModel.CoverageSegments, ViewModel.NotCleanedGaps, ViewModel.Today);

    // WinUI allows one open ContentDialog; a quick second click while the first
    // is opening would otherwise throw.
    private bool _isConfirmingDiscard;

    private async void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        if (_isConfirmingDiscard
            || (sender as FrameworkElement)?.DataContext is not LibraryCardViewModel { Cleanup: { } cleanup } card)
            return;
        _isConfirmingDiscard = true;
        try
        {
            if (await DiscardDialog.ConfirmAsync(XamlRoot, card.RangeLabel))
                ViewModel.Discard(cleanup);
        }
        finally
        {
            _isConfirmingDiscard = false;
        }
    }

    // Pill and border brushes are looked up once when a card is created, so the
    // cards are rebuilt to pick up the new theme's brushes.
    private void RecreateCardsWithNewThemeBrushes()
    {
        Cards.ItemsSource = null;
        Cards.ItemsSource = ViewModel.Cards;
    }
}
