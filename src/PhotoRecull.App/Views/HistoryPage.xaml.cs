using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoRecull.App.ViewModels;

namespace PhotoRecull.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage() => InitializeComponent();

    public HistoryViewModel ViewModel { get; } = new();

    // WinUI allows one open ContentDialog; a quick second click would otherwise throw.
    private bool _isConfirmingRemove;

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_isConfirmingRemove || (sender as FrameworkElement)?.DataContext is not HistoryRunViewModel run)
            return;
        _isConfirmingRemove = true;
        try
        {
            var dialog = ConfirmDialogContent.Create(XamlRoot, "Remove from History?");
            dialog.PrimaryButtonText = "Remove";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = ContentDialogButton.Close;
            dialog.Content = new TextBlock
            {
                Text = $"{run.Title}, {run.WhenText}. PhotoRecull forgets this entry; no files are moved back or deleted.",
                TextWrapping = TextWrapping.Wrap,
                Width = 480,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                ViewModel.Remove(run);
                Bindings.Update();
            }
        }
        finally
        {
            _isConfirmingRemove = false;
        }
    }
}
