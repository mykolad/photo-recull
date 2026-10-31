using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoRecull.App.ViewModels;

namespace PhotoRecull.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage() => InitializeComponent();

    public SettingsViewModel ViewModel { get; } = new();

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.StopSaving();
}
