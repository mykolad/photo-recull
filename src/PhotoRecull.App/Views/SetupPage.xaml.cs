using Microsoft.UI.Xaml.Controls;
using PhotoRecull.App.ViewModels;

namespace PhotoRecull.App.Views;

public sealed partial class SetupPage : Page
{
    public SetupPage() => InitializeComponent();

    public SetupViewModel ViewModel { get; } = new();
}
