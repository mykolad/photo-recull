using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoRecull.App.ViewModels;

namespace PhotoRecull.App.Views;

public sealed partial class StagedReviewPage : Page
{
    public StagedReviewPage() => InitializeComponent();

    public StagedReviewViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = new StagedReviewViewModel((Guid)e.Parameter);
        ViewModel.AllHandled += (_, _) =>
        {
            ShellPage.Current?.ShowLibrary();
            ShellPage.Current?.ShowToast($"{ViewModel.Title} is now marked Done in Library. {ViewModel.ResultMessage}");
        };
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StagedReviewViewModel.IsActing))
                ShellPage.Current?.SetNavigationLocked(ViewModel.IsActing);
        };
        Bindings.Update();
        await ViewModel.LoadAsync();
#if DEBUG
        if (Services.DebugScreenshot.RequestedCleanupStep() is { Step: "staged-moveback" })
            await ViewModel.MoveAllBackCommand.ExecuteAsync(null);
#endif
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Close();

    private void OnBackToLibraryClick(object sender, RoutedEventArgs e) => ShellPage.Current?.ShowLibrary();

    // WinUI allows one open ContentDialog; a quick second click would otherwise throw.
    private bool _isConfirming;

    private async void OnDeleteAllClick(object sender, RoutedEventArgs e)
    {
        if (_isConfirming)
            return;
        _isConfirming = true;
        try
        {
            int count = ViewModel.Photos.Count;
            var dialog = ConfirmDialogContent.Create(XamlRoot, $"Delete {count:N0} staged photo{(count == 1 ? "" : "s")}?");
            dialog.PrimaryButtonText = "Delete";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = ContentDialogButton.Close;
            dialog.Content = new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = ViewModel.DeletesArePermanent ? InfoBarSeverity.Error : InfoBarSeverity.Warning,
                Message = ViewModel.DeletesArePermanent
                    ? "Some stage folders are on a network drive, which skips the Windows Recycle Bin: deleting there is permanent unless DSM's Recycle Bin is on for the shared folder. Move back any photo you want to keep first."
                    : "They go to the Recycle Bin. Move back any photo you want to keep first.",
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.DeleteAllAsync();
        }
        finally
        {
            _isConfirming = false;
        }
    }
}
