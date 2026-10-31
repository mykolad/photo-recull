using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoRecull.App.ViewModels;

namespace PhotoRecull.App.Views;

public sealed partial class TargetFolderEditor : UserControl
{
    public static readonly DependencyProperty FolderProperty = DependencyProperty.Register(
        nameof(Folder), typeof(TargetFolderViewModel), typeof(TargetFolderEditor), new PropertyMetadata(null));

    public TargetFolderEditor()
    {
        InitializeComponent();
        // The validation text brush is looked up for the current theme, so rerun
        // the bindings to pick up the new theme's brush.
        ActualThemeChanged += (_, _) => Bindings.Update();
    }

    public TargetFolderViewModel? Folder
    {
        get => (TargetFolderViewModel?)GetValue(FolderProperty);
        set => SetValue(FolderProperty, value);
    }
}
