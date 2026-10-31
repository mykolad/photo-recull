using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

// A target's folder as typed or browsed to, checked as the user types.
public sealed partial class TargetFolderViewModel : ObservableObject
{
    private CancellationTokenSource? _folderValidation;

    public TargetFolderViewModel(TargetKind kind, string folderPath)
    {
        Kind = kind;
        FolderPath = folderPath;
    }

    public TargetKind Kind { get; }

    public string Name => SyncTargets.DisplayName(Kind);

    public string Glyph => ShellPage.TargetGlyph(Kind);

    public string FolderLabel => Kind == TargetKind.OneDrive ? "Camera Roll folder" : "Backup folder";

    public string PlaceholderText => Kind == TargetKind.OneDrive
        ? @"C:\Users\you\OneDrive\Pictures\Camera Roll"
        : @"\\nas\home\Photos\MobileBackup\iPhone";

    public string FolderHint => Kind == TargetKind.OneDrive
        ? "Local folder or mapped drive"
        : "Network folder or mapped drive that File Explorer can already open. Its year and month folders are read too.";

    [ObservableProperty]
    public partial string FolderPath { get; set; } = "";

    [ObservableProperty]
    public partial string? FolderValidationText { get; set; }

    [ObservableProperty]
    public partial bool IsFolderValid { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingFolder { get; set; }

    public TargetSettings Settings => new(Kind, FolderPath.Trim());

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (await Pickers.PickFolderAsync() is { } path)
            FolderPath = path;
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
            IsCheckingFolder = false;
            return;
        }
        FolderValidationText = "Checking folder…";
        IsCheckingFolder = true;

        string resultText;
        bool isValid;
        try
        {
            // Wait for typing to pause, then stop counting as soon as a newer path
            // supersedes this one, so a slow mapped drive isn't listed once per keystroke.
            await Task.Delay(TimeSpan.FromMilliseconds(300), validation.Token);
            string? found = await Task.Run(() => DescribeFolderOrNullIfMissing(folderPath.Trim(), validation.Token), validation.Token);
            isValid = found is not null;
            resultText = found is not null ? $"Folder found · {found}"
                : Kind == TargetKind.SynologyPhotos ? "Folder not found. Check the path, that the NAS is on, and that File Explorer can open it."
                : "Folder not found. Check the path, or that the drive is mapped.";
        }
        catch (OperationCanceledException)
        {
            return;
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

    // A Synology backup is checked by its year folders: counting every file in
    // them would read the whole library over the network.
    private string? DescribeFolderOrNullIfMissing(string folderPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folderPath))
            return null;
        if (Kind == TargetKind.SynologyPhotos)
        {
            var years = Directory.EnumerateDirectories(folderPath)
                .Select(Path.GetFileName)
                .Where(name => name is { Length: 4 } && name.All(char.IsAsciiDigit))
                .Select(name => int.Parse(name!, CultureInfo.InvariantCulture))
                .Order()
                .ToList();
            if (years.Count > 0)
                return years.Count == 1 ? $"photos from {years[0]}" : $"photos from {years[0]}–{years[^1]}";
        }
        int count = 0;
        foreach (var _ in Directory.EnumerateFiles(folderPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
        }
        return $"{count:N0} files";
    }
}
