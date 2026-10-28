using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoRecull.App.ViewModels;

namespace PhotoRecull.App.Views;

public sealed partial class CleanupFlowPage : Page
{
    public CleanupFlowPage()
    {
        InitializeComponent();
        // The flow needs the width, so the pane collapses to icons while it's open.
        Loaded += (_, _) => ShellPage.Current?.CollapseNavigationPane();
    }

    public CleanupFlowViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = new CleanupFlowViewModel((CleanupFlowRequest)e.Parameter);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.RangeChanged += (_, _) => ShowRangeComparison();
        ViewModel.TitleChanged += (_, _) => App.MainWindow.ShowRangeInTitle(ViewModel.Title);
        ViewModel.BackToLibraryRequested += (_, _) => ShellPage.Current?.ShowLibrary();
        Bindings.Update();

        App.MainWindow.ShowRangeInTitle(ViewModel.Title);
        Stepper.CurrentStepIndex = (int)ViewModel.Step;
#if DEBUG
        if (Services.DebugScreenshot.RequestedCleanupStep() is { } debugRequest)
        {
            _ = Services.DebugScreenshot.DriveFlowToStepAsync(ViewModel, debugRequest.Step);
            return;
        }
#endif
        ViewModel.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.CancelRunningWork();
        App.MainWindow.ShowRangeInTitle(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupFlowViewModel.Step))
            Stepper.CurrentStepIndex = (int)ViewModel.Step;
    }

    private void ShowRangeComparison()
    {
        if (ViewModel is { EarliestPickedDay: { } earliest, LatestPickedDay: { } latest })
            ComparisonBars.Show(earliest, latest, ViewModel.DraftRange());
    }
}
