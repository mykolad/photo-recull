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
        Coverage.SegmentClicked += (_, segment) => ViewModel.OpenCleanupAt(segment.From, segment.To);
        ActualThemeChanged += (_, _) => RecreateCardsWithNewThemeBrushes();
    }

    public LibraryViewModel ViewModel { get; } = new();

    private void ShowCoverageForSelectedYear() =>
        Coverage.Show(ViewModel.CoverageYear, ViewModel.CoverageSegments, ViewModel.NotCleanedGaps, ViewModel.Today);

    // Pill and border brushes are looked up once when a card is created, so the
    // cards are rebuilt to pick up the new theme's brushes.
    private void RecreateCardsWithNewThemeBrushes()
    {
        Cards.ItemsSource = null;
        Cards.ItemsSource = ViewModel.Cards;
    }
}
