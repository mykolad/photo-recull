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
