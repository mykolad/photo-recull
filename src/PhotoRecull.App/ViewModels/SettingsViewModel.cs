using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public sealed partial class SettingsViewModel
{
    public SettingsViewModel()
    {
        foreach (var saved in AppServices.Database.LoadSettings().Targets)
            Targets.Add(NewRow(saved));
        RefreshWhichTargetsCanBeRemoved();
    }

    private TargetSettingsRowViewModel NewRow(TargetSettings? saved) =>
        new(saved, SaveTargets, RemoveTarget, OtherTargetOverlapping, OtherTargetNames);

    // Two targets reading the same folder would list each photo twice, and could
    // stage or delete one file on behalf of both.
    private TargetSettings? OtherTargetOverlapping(TargetSettingsRowViewModel row, string folder) =>
        Targets
            .Where(other => other != row)
            .Select(other => other.Saved)
            .FirstOrDefault(saved => saved is not null
                && (FileActions.IsSameOrInside(folder, saved.FolderPath) || FileActions.IsSameOrInside(saved.FolderPath, folder)));

    private List<string> OtherTargetNames(TargetSettingsRowViewModel row) =>
        Targets.Where(other => other != row).Select(other => other.Folder.Name).ToList();

    public ObservableCollection<TargetSettingsRowViewModel> Targets { get; } = [];

    public GooglePhotosSettingsViewModel GooglePhotos { get; } = new();

    private bool _isClosed;

    // A folder check still running when the page is left would otherwise save
    // later, over changes made since on a newer Settings page.
    // A sign-in still waiting for the browser is cancelled too: finishing later, it
    // would update this page's view model, not the one shown next time.
    public void StopSaving()
    {
        _isClosed = true;
        GooglePhotos.SignInCancelCommand.Execute(null);
    }

    // A new folder is saved once it's found, like a changed one.
    [RelayCommand]
    private void AddTarget()
    {
        Targets.Add(NewRow(null));
        RefreshWhichTargetsCanBeRemoved();
    }

    private void RemoveTarget(TargetSettingsRowViewModel row)
    {
        Targets.Remove(row);
        if (row.Saved is not null)
            SaveTargets();
        else
            RefreshWhichTargetsCanBeRemoved();
    }

    private void SaveTargets()
    {
        if (_isClosed)
            return;
        var settings = AppServices.Database.LoadSettings() with
        {
            Targets = Targets.Select(row => row.Saved).OfType<TargetSettings>().ToList(),
        };
        AppServices.Database.SaveSettings(settings);
        RefreshWhichTargetsCanBeRemoved();
        ShellPage.Current?.RefreshConnectedServices();
    }

    // A cleanup needs a target to scan, so the last one saved stays.
    private void RefreshWhichTargetsCanBeRemoved()
    {
        foreach (var row in Targets)
            row.CanBeRemoved = row.Saved is null || Targets.Any(other => other != row && other.Saved is not null);
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

// One target in Settings: its folder and name. A folder is saved once it's
// found, so a half-typed or unreachable path never replaces a working one.
public sealed partial class TargetSettingsRowViewModel : ObservableObject
{
    private readonly Action _saveTargets;
    private readonly Action<TargetSettingsRowViewModel> _remove;
    private readonly Func<TargetSettingsRowViewModel, string, TargetSettings?> _otherTargetOverlapping;
    private TargetSettings? _refusedForOverlapping;

    public TargetSettingsRowViewModel(
        TargetSettings? saved, Action saveTargets, Action<TargetSettingsRowViewModel> remove,
        Func<TargetSettingsRowViewModel, string, TargetSettings?> otherTargetOverlapping,
        Func<TargetSettingsRowViewModel, IReadOnlyCollection<string>> otherTargetNames)
    {
        _saveTargets = saveTargets;
        _remove = remove;
        _otherTargetOverlapping = otherTargetOverlapping;
        Saved = saved;
        Folder = new TargetFolderViewModel(saved, () => otherTargetNames(this));
        Folder.PropertyChanged += OnFolderChanged;
    }

    public TargetFolderViewModel Folder { get; }

    public TargetSettings? Saved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial bool CanBeRemoved { get; set; }

    [RelayCommand(CanExecute = nameof(CanBeRemoved))]
    private void Remove() => _remove(this);

    public string StatusText => _refusedForOverlapping is { } other
            ? $"Not saved: this folder overlaps {other.Name}'s, so its photos would be scanned twice."
        : Saved is null ? "Saved once the folder is found."
        : Saved.FolderPath != Folder.Settings.FolderPath ? $"Still using {Saved.FolderPath} until the new folder is found."
        : !CanBeRemoved ? "Saved. A cleanup needs a target, so this one stays until you add another."
        : "Saved. New cleanups can use it.";

    private void OnFolderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TargetFolderViewModel.IsFolderValid) or nameof(TargetFolderViewModel.Name)
                or nameof(TargetFolderViewModel.OnlyIphonePhotos) && Folder.IsFolderValid)
            SaveAs(Folder.Settings);
        else if (e.PropertyName == nameof(TargetFolderViewModel.FolderPath))
        {
            _refusedForOverlapping = null;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void SaveAs(TargetSettings target)
    {
        _refusedForOverlapping = _otherTargetOverlapping(this, target.FolderPath);
        if (_refusedForOverlapping is not null)
        {
            OnPropertyChanged(nameof(StatusText));
            return;
        }
        if (Saved == target)
            return;
        Saved = target;
        OnPropertyChanged(nameof(StatusText));
        _saveTargets();
    }
}
