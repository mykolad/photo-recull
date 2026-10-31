using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public sealed class SettingsViewModel
{
    public SettingsViewModel()
    {
        var savedTargets = AppServices.Database.LoadSettings().Targets;
        Targets = [.. new[] { TargetKind.OneDrive, TargetKind.SynologyPhotos }
            .Select(kind => new TargetSettingsRowViewModel(kind, savedTargets.FirstOrDefault(t => t.Kind == kind), SaveTargets))];
        RefreshWhichTargetsCanBeTurnedOff();
    }

    public List<TargetSettingsRowViewModel> Targets { get; }

    private bool _isClosed;

    // A folder check still running when the page is left would otherwise save
    // later, over changes made since on a newer Settings page.
    public void StopSaving() => _isClosed = true;

    private void SaveTargets(TargetSettingsRowViewModel changed)
    {
        if (_isClosed)
            return;
        var current = AppServices.Database.LoadSettings();
        var settings = current with
        {
            Targets = current.Targets
                .Where(t => t.Kind != changed.Folder.Kind)
                .Concat(changed.Saved is { } saved ? [saved] : [])
                .OrderBy(t => t.Kind)
                .ToList(),
        };
        AppServices.Database.SaveSettings(settings);
        RefreshWhichTargetsCanBeTurnedOff();
        ShellPage.Current?.RefreshConnectedServices();
    }

    // A cleanup needs a target to scan, so the last one in use stays on.
    private void RefreshWhichTargetsCanBeTurnedOff()
    {
        foreach (var row in Targets)
            row.CanBeTurnedOff = !row.IsUsed || Targets.Any(other => other != row && other.Saved is not null);
    }
}

// One target in Settings: on or off, and its folder. A folder is saved once it's
// found, so a half-typed or unreachable path never replaces a working one.
public sealed partial class TargetSettingsRowViewModel : ObservableObject
{
    private readonly Action<TargetSettingsRowViewModel> _saveTargets;

    public TargetSettingsRowViewModel(TargetKind kind, TargetSettings? saved, Action<TargetSettingsRowViewModel> saveTargets)
    {
        _saveTargets = saveTargets;
        Saved = saved;
        Folder = new TargetFolderViewModel(kind, saved?.FolderPath ?? "");
        Folder.PropertyChanged += OnFolderChanged;
        IsUsed = saved is not null;
    }

    public TargetFolderViewModel Folder { get; }

    public TargetSettings? Saved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsUsed { get; set; }

    [ObservableProperty]
    public partial bool CanBeTurnedOff { get; set; }

    public string StatusText => !IsUsed ? "Off. New cleanups can't use it."
        : Saved is null ? "Saved once the folder is found."
        : Saved.FolderPath != Folder.Settings.FolderPath ? $"Still using {Saved.FolderPath} until the new folder is found."
        : "Saved. New cleanups can use it.";

    partial void OnIsUsedChanged(bool value)
    {
        if (!value)
            SaveAs(null);
        else if (Folder.IsFolderValid)
            SaveAs(Folder.Settings);
    }

    private void OnFolderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetFolderViewModel.IsFolderValid) && Folder.IsFolderValid && IsUsed)
            SaveAs(Folder.Settings);
        else if (e.PropertyName == nameof(TargetFolderViewModel.FolderPath))
            OnPropertyChanged(nameof(StatusText));
    }

    private void SaveAs(TargetSettings? target)
    {
        if (Saved == target)
            return;
        Saved = target;
        OnPropertyChanged(nameof(StatusText));
        _saveTargets(this);
    }
}
