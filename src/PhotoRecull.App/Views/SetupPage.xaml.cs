using Microsoft.UI.Xaml.Controls;
using PhotoRecull.App.ViewModels;

namespace PhotoRecull.App.Views;

public sealed partial class SetupPage : Page
{
    public SetupPage()
    {
        InitializeComponent();
        // The validation text brush is looked up for the current theme, so rerun
        // the bindings to pick up the new theme's brush.
        ActualThemeChanged += (_, _) => Bindings.Update();
    }

    public SetupViewModel ViewModel { get; } = new();
}
