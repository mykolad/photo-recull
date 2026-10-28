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
    }

    public LibraryViewModel ViewModel { get; } = new();

    private void ShowCoverageForSelectedYear() =>
        Coverage.Show(ViewModel.CoverageYear, ViewModel.CoverageSegments, ViewModel.Today);
}
