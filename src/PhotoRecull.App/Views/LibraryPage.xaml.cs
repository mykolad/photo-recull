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
    }

    public LibraryViewModel ViewModel { get; } = new();

    private void ShowCoverageForSelectedYear() =>
        Coverage.Show(ViewModel.CoverageYear, ViewModel.CoverageSegments, ViewModel.NotCleanedGaps, ViewModel.Today);

    private async void OnDiscardClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryCardViewModel { Cleanup: { } cleanup } card
            || !await DiscardDialog.ConfirmAsync(XamlRoot, card.RangeLabel))
            return;
        ViewModel.Discard(cleanup);
    }

    // Pill and border brushes are looked up once when a card is created, so the
    // cards are rebuilt to pick up the new theme's brushes.
    private void RecreateCardsWithNewThemeBrushes()
    {
        Cards.ItemsSource = null;
        Cards.ItemsSource = ViewModel.Cards;
    }
}
