using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public sealed class SettingsViewModel
{
    public SettingsViewModel()
    {
        var savedTargets = AppServices.Database.LoadSettings().Targets;
        Targets = [.. new[] { TargetKind.OneDrive, TargetKind.SynologyPhotos }
            .Select(kind => new TargetSettingsRowViewModel(
                kind, savedTargets.FirstOrDefault(t => t.Kind == kind), SaveTargets, OtherTargetOverlapping))];
        RefreshWhichTargetsCanBeTurnedOff();
    }

    // Two targets reading the same folder would list each photo twice, and could
    // stage or delete one file on behalf of both.
    private TargetSettings? OtherTargetOverlapping(TargetSettingsRowViewModel row, string folder) =>
        Targets
            .Where(other => other != row)
            .Select(other => other.Saved)
            .FirstOrDefault(saved => saved is not null
                && (FileActions.IsSameOrInside(folder, saved.FolderPath) || FileActions.IsSameOrInside(saved.FolderPath, folder)));

    public List<TargetSettingsRowViewModel> Targets { get; }

    public GooglePhotosSettingsViewModel GooglePhotos { get; } = new();

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

// The source: signing out lets the next pick use another Google account. Cleanups
// stay as they are: a pick keeps only capture times, tied to no account. The
// browser chosen here is used to sign in and to open the Photos Picker.
public sealed partial class GooglePhotosSettingsViewModel : ObservableObject
{
    public GooglePhotosSettingsViewModel()
    {
        var signInBrowser = AppServices.Database.LoadSettings().SignInBrowser;
        SelectedSignInBrowserIndex = SignInBrowserChoice.IndexFor(signInBrowser);
        if (SignInBrowserChoices[SelectedSignInBrowserIndex].IsCustomPath)
            CustomBrowserPath = signInBrowser!;
        _isLoaded = true;
    }

    // Loading the saved choice sets the index before the custom path; saving then
    // would replace a custom path with the default.
    private readonly bool _isLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CanSignIn))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    public partial bool IsSignedIn { get; set; } = AppServices.Google.IsSignedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CanSignIn))]
    public partial bool IsWaitingForBrowser { get; set; }

    public bool CanSignIn => !IsSignedIn && !IsWaitingForBrowser;

    public string StatusText =>
        IsSignedIn ? "Signed in. Sign out to pick photos from another Google account."
        : IsWaitingForBrowser ? $"Finish signing in to Google in {SignInBrowserChoice.DisplayNameFor(SignInBrowser)}."
        : "Not signed in. Sign in here, or you'll be asked the next time you pick photos.";

    [ObservableProperty]
    public partial string? SignInError { get; set; }

    public IReadOnlyList<SignInBrowserChoice> SignInBrowserChoices => SignInBrowserChoice.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomBrowserPathVisible))]
    public partial int SelectedSignInBrowserIndex { get; set; }

    [ObservableProperty]
    public partial string CustomBrowserPath { get; set; } = "";

    public bool IsCustomBrowserPathVisible => SignInBrowserChoices[Math.Max(0, SelectedSignInBrowserIndex)].IsCustomPath;

    private string? SignInBrowser => SignInBrowserChoice.SettingFor(SelectedSignInBrowserIndex, CustomBrowserPath);

    partial void OnSelectedSignInBrowserIndexChanged(int value) => SaveSignInBrowser();

    partial void OnCustomBrowserPathChanged(string value) => SaveSignInBrowser();

    private void SaveSignInBrowser()
    {
        if (!_isLoaded)
            return;
        var settings = AppServices.Database.LoadSettings();
        if (settings.SignInBrowser != SignInBrowser)
            AppServices.Database.SaveSettings(settings with { SignInBrowser = SignInBrowser });
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        SignInError = null;
        IsWaitingForBrowser = true;
        try
        {
            await AppServices.Google.SignInAsync(
                SignInBrowser, AppServices.Database.LoadSettings().DeveloperCredentialsPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SignInError = $"Couldn't sign in to Google: {ex.Message}";
        }
        finally
        {
            IsWaitingForBrowser = false;
            IsSignedIn = AppServices.Google.IsSignedIn;
            ShellPage.Current?.RefreshConnectedServices();
        }
    }

    [RelayCommand(CanExecute = nameof(IsSignedIn))]
    private async Task SignOutAsync()
    {
        await AppServices.Google.SignOutAsync(onSignedOut: () =>
        {
            IsSignedIn = AppServices.Google.IsSignedIn;
            ShellPage.Current?.RefreshConnectedServices();
        });
    }
}

// One target in Settings: on or off, and its folder. A folder is saved once it's
// found, so a half-typed or unreachable path never replaces a working one.
public sealed partial class TargetSettingsRowViewModel : ObservableObject
{
    private readonly Action<TargetSettingsRowViewModel> _saveTargets;
    private readonly Func<TargetSettingsRowViewModel, string, TargetSettings?> _otherTargetOverlapping;
    private TargetSettings? _refusedForOverlapping;

    public TargetSettingsRowViewModel(
        TargetKind kind, TargetSettings? saved, Action<TargetSettingsRowViewModel> saveTargets,
        Func<TargetSettingsRowViewModel, string, TargetSettings?> otherTargetOverlapping)
    {
        _saveTargets = saveTargets;
        _otherTargetOverlapping = otherTargetOverlapping;
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
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool CanBeTurnedOff { get; set; }

    public string StatusText => !IsUsed ? "Off. New cleanups can't use it."
        : _refusedForOverlapping is { } other
            ? $"Not saved: this folder overlaps {SyncTargets.DisplayName(other.Kind)}'s, so its photos would be scanned twice."
        : Saved is null ? "Saved once the folder is found."
        : Saved.FolderPath != Folder.Settings.FolderPath ? $"Still using {Saved.FolderPath} until the new folder is found."
        : !CanBeTurnedOff ? "Saved. It's the only target on, and a cleanup needs one: turn on another to turn this one off."
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
        {
            _refusedForOverlapping = null;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void SaveAs(TargetSettings? target)
    {
        _refusedForOverlapping = target is null ? null : _otherTargetOverlapping(this, target.FolderPath);
        if (_refusedForOverlapping is not null)
        {
            OnPropertyChanged(nameof(StatusText));
            return;
        }
        if (Saved == target)
            return;
        Saved = target;
        OnPropertyChanged(nameof(StatusText));
        _saveTargets(this);
    }
}
