using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using PhotoRecull.App.Services;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.Views;

public sealed partial class TargetChoice(TargetSettings target, Action selectionChanged) : ObservableObject
{
    public TargetSettings Target { get; } = target;

    public string Glyph => ShellPage.TargetGlyph(Target.Kind);

    public string Name => SyncTargets.DisplayName(Target.Kind);

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    partial void OnIsSelectedChanged(bool value) => selectionChanged();
}

public sealed partial class NewCleanupDialog : ContentDialog, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public NewCleanupDialog()
    {
        var settings = AppServices.Database.LoadSettings();
        TargetChoices = settings.Targets
            .Select(t => new TargetChoice(t, () => OnPropertyChanged(nameof(HasSelectedTarget))))
            .ToList();
        var lastCleanupEnd = CleanupNeighbors.For(AppServices.Database.LoadCleanups(), null).PreviousCleanupEnd;
        PickerStartHint = lastCleanupEnd is { } end
            ? $"Your last cleanup ends on {end:d MMM yyyy}. In the picker, start from {end.AddDays(1):d MMM yyyy}."
            : null;
        InitializeComponent();
    }

    public IReadOnlyList<TargetChoice> TargetChoices { get; }

    public string? PickerStartHint { get; }

    public bool HasSelectedTarget => TargetChoices.Any(t => t.IsSelected);

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public IReadOnlyList<TargetSettings> SelectedTargets => TargetChoices.Where(t => t.IsSelected).Select(t => t.Target).ToList();
}
