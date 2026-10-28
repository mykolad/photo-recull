using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PhotoRecull.App.ViewModels;
using VirtualKey = Windows.System.VirtualKey;

namespace PhotoRecull.App.Views;

public sealed partial class CleanupFlowPage : Page
{
    public CleanupFlowPage()
    {
        InitializeComponent();
        // The flow needs the width, so the pane collapses to icons while it's open.
        Loaded += (_, _) => ShellPage.Current?.CollapseNavigationPane();
        KeyDown += OnPreviewKeyDown;
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
        if (e.PropertyName == nameof(CleanupFlowViewModel.Review))
            ReviewTabs.SelectedItem = ToRemoveTab;
    }

    private void ShowRangeComparison()
    {
        if (ViewModel is { EarliestPickedDay: { } earliest, LatestPickedDay: { } latest })
            ComparisonBars.Show(earliest, latest, ViewModel.DraftRange());
    }

    // ---------------------------------------------------------------- Review

    private void OnReviewTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var review = ViewModel?.Review;
        if (review is null)
            return;
        switch (sender.Items.IndexOf(sender.SelectedItem))
        {
            case 0: review.ShowToRemoveCommand.Execute(null); break;
            case 1: review.ShowToKeepCommand.Execute(null); break;
            case 2: review.ShowSkippedCommand.Execute(null); break;
        }
    }

    private async void OnStageClick(object sender, RoutedEventArgs e) => await StageAsync();

    private async Task StageAsync()
    {
        ViewModel.BeginConfirm();
        var dialog = new StageDialog(XamlRoot, ViewModel);
        if (await dialog.ConfirmAsync())
            await ViewModel.StageAsync(dialog.StageFolders);
        else
            ViewModel.CancelConfirm();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginConfirm();
        var choice = await new DeleteDialog(XamlRoot, ViewModel, ViewModel.Review?.KeptByUserCount ?? 0).ChooseAsync();
        switch (choice)
        {
            case DeleteChoice.Delete:
                await ViewModel.DeleteAsync();
                break;
            case DeleteChoice.StageInstead:
                await StageAsync();
                break;
            default:
                ViewModel.CancelConfirm();
                break;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel?.Review is not { IsPreviewOpen: true } review)
            return;
        var command = e.Key switch
        {
            VirtualKey.Left => review.ShowPreviousPreviewCommand,
            VirtualKey.Right => review.ShowNextPreviewCommand,
            VirtualKey.K => review.ToggleKeepInPreviewCommand,
            VirtualKey.O => review.OpenOriginalInPreviewCommand,
            VirtualKey.Escape => review.ClosePreviewCommand,
            _ => null,
        };
        if (command is null)
            return;
        command.Execute(null);
        e.Handled = true;
    }
}
