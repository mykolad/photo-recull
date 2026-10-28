using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.App.Services;
using PhotoRecull.Core;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public enum GoogleConnection
{
    NotConnected,
    WaitingForBrowser,
    Connected,
}

public sealed partial class SetupViewModel : ObservableObject
{
    private CancellationTokenSource? _folderValidation;

    public SetupViewModel()
    {
        GoogleConnection = AppServices.Google.IsSignedIn ? GoogleConnection.Connected : GoogleConnection.NotConnected;
        FolderPath = SuggestedOneDriveCameraRollOrEmpty();
    }

    public bool NeedsDeveloperCredentialsFile { get; } = !AppServices.Google.HasBuiltInOAuthClient;

    [ObservableProperty]
    public partial string DeveloperCredentialsPath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFinishSetup))]
    public partial bool IsDeveloperCredentialsFileValid { get; set; }

    // In a developer build the cached Google token can survive a restart while the
    // credentials choice doesn't, so being connected alone isn't enough to finish.
    private bool HasUsableOAuthClient => !NeedsDeveloperCredentialsFile || IsDeveloperCredentialsFileValid;

    [ObservableProperty]
    public partial string? DeveloperCredentialsError { get; set; }

    public IReadOnlyList<SignInBrowserChoice> SignInBrowserChoices => SignInBrowserChoice.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomBrowserPathVisible), nameof(SignInStatusText))]
    public partial int SelectedSignInBrowserIndex { get; set; }

    [ObservableProperty]
    public partial string CustomBrowserPath { get; set; } = "";

    public bool IsCustomBrowserPathVisible => SelectedSignInBrowser.IsCustomPath;

    private SignInBrowserChoice SelectedSignInBrowser => SignInBrowserChoices[Math.Max(0, SelectedSignInBrowserIndex)];

    private string? SignInBrowser => SelectedSignInBrowser.IsCustomPath
        ? (string.IsNullOrWhiteSpace(CustomBrowserPath) ? null : CustomBrowserPath.Trim())
        : SelectedSignInBrowser.BrowserLauncherValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(IsWaitingForBrowser), nameof(SignInStatusText),
        nameof(SignInButtonText), nameof(CanFinishSetup))]
    public partial GoogleConnection GoogleConnection { get; set; }

    [ObservableProperty]
    public partial string? SignInError { get; set; }

    public bool IsConnected => GoogleConnection == GoogleConnection.Connected;

    public bool IsWaitingForBrowser => GoogleConnection == GoogleConnection.WaitingForBrowser;

    public string SignInStatusText => GoogleConnection switch
    {
        GoogleConnection.WaitingForBrowser => $"Finish signing in to Google in {SelectedSignInBrowser.DisplayNameInSentence}.",
        GoogleConnection.Connected => "Connected to Google.",
        _ => "Sign in to Google to allow PhotoRecull to open the Photos Picker.",
    };

    public string SignInButtonText => IsWaitingForBrowser ? "Waiting for Google…" : "Sign in with Google";

    [ObservableProperty]
    public partial string FolderPath { get; set; } = "";

    [ObservableProperty]
    public partial string? FolderValidationText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFinishSetup))]
    public partial bool IsFolderValid { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingFolder { get; set; }

    public bool CanFinishSetup => IsConnected && IsFolderValid && HasUsableOAuthClient;

    [RelayCommand]
    private async Task BrowseDeveloperCredentialsAsync()
    {
        if (await Pickers.PickFileAsync(".json") is not { } path)
            return;

        DeveloperCredentialsPath = path;
        try
        {
            var oauthClient = await GooglePhotosPickerClient.LoadOAuthClientFromCredentialsFileAsync(path);
            IsDeveloperCredentialsFileValid =
                !string.IsNullOrEmpty(oauthClient?.ClientId) && !string.IsNullOrEmpty(oauthClient.ClientSecret);
        }
        catch (Exception)
        {
            // Any failure to read it as Google client secrets (unreadable file,
            // malformed JSON, not a Desktop/Web client) means it can't be used.
            IsDeveloperCredentialsFileValid = false;
        }
        DeveloperCredentialsError = IsDeveloperCredentialsFileValid
            ? null
            : "This isn't an OAuth client file. Download the Desktop app client's JSON from Google Cloud Console.";
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        SignInError = null;
        if (NeedsDeveloperCredentialsFile && !IsDeveloperCredentialsFileValid)
        {
            DeveloperCredentialsError ??= "Choose the credentials.json you downloaded.";
            return;
        }

        GoogleConnection = GoogleConnection.WaitingForBrowser;
        try
        {
            await AppServices.Google.SignInAsync(SignInBrowser, NullIfEmpty(DeveloperCredentialsPath), cancellationToken);
            GoogleConnection = GoogleConnection.Connected;
        }
        catch (OperationCanceledException)
        {
            GoogleConnection = GoogleConnection.NotConnected;
        }
        catch (Exception ex)
        {
            GoogleConnection = GoogleConnection.NotConnected;
            SignInError = $"Couldn't sign in to Google: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (await Pickers.PickFolderAsync() is { } path)
            FolderPath = path;
    }

    [RelayCommand]
    private void FinishSetup()
    {
        var settings = AppServices.SettingsStore.LoadOrCreateDefault() with
        {
            SignInBrowser = SignInBrowser,
            Targets = [new TargetSettings(TargetKind.OneDrive, FolderPath.Trim())],
            DeveloperCredentialsPath = NullIfEmpty(DeveloperCredentialsPath),
            SetupCompletedAt = DateTimeOffset.Now,
        };
        AppServices.SettingsStore.Save(settings);
        App.MainWindow.ShowShell("Setup complete. Start your first cleanup.");
    }

    partial void OnFolderPathChanged(string value) => _ = ValidateFolderAsync(value);

    private async Task ValidateFolderAsync(string folderPath)
    {
        _folderValidation?.Cancel();
        var validation = _folderValidation = new CancellationTokenSource();

        IsFolderValid = false;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            FolderValidationText = null;
            return;
        }
        FolderValidationText = "Checking folder…";
        IsCheckingFolder = true;

        string resultText;
        bool isValid;
        try
        {
            int? fileCount = await Task.Run(() =>
                Directory.Exists(folderPath) ? Directory.EnumerateFiles(folderPath).Count() : (int?)null);
            isValid = fileCount is not null;
            resultText = fileCount is { } count
                ? $"Folder found · {count:N0} files"
                : "Folder not found. Check the path, or that the drive is mapped.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            isValid = false;
            resultText = $"Can't read this folder: {ex.Message}";
        }

        if (validation.IsCancellationRequested)
            return;
        IsFolderValid = isValid;
        IsCheckingFolder = false;
        FolderValidationText = resultText;
    }

    private static string SuggestedOneDriveCameraRollOrEmpty()
    {
        var oneDriveRoot = Environment.GetEnvironmentVariable("OneDriveConsumer")
            ?? Environment.GetEnvironmentVariable("OneDrive");
        if (oneDriveRoot is null)
            return "";
        var cameraRoll = Path.Combine(oneDriveRoot, "Pictures", "Camera Roll");
        return Directory.Exists(cameraRoll) ? cameraRoll : "";
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
