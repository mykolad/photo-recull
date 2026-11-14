using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

// A target's folder as typed or browsed to, checked as the user types, and its
// name: suggested from the folder (OneDrive, Synology Photos, the NAS) until the
// user types one.
public sealed partial class TargetFolderViewModel : ObservableObject
{
    private readonly Func<IReadOnlyCollection<string>> _otherTargetNames;
    private CancellationTokenSource? _folderValidation;
    private bool _isNameTyped;
    private bool _isSuggestingName;

    public TargetFolderViewModel(TargetSettings? saved, Func<IReadOnlyCollection<string>> otherTargetNames, string suggestedFolderPath = "")
    {
        _otherTargetNames = otherTargetNames;
        _isNameTyped = saved is not null;
        Name = saved?.Name ?? "";
        OnlyIphonePhotos = saved?.OnlyIphonePhotos ?? true;
        FolderPath = saved?.FolderPath ?? suggestedFolderPath;
        if (!_isNameTyped)
            SuggestName();
    }

    public string Glyph => ShellPage.TargetGlyph(Settings);

    public string PlaceholderText => @"C:\Users\you\OneDrive\Pictures\Camera Roll";

    public string FolderHint =>
        "The folder your iPhone photos are backed up to: OneDrive's Camera Roll, a NAS folder, or any other folder File Explorer can open. Its subfolders are read too.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    public partial string FolderPath { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial bool OnlyIphonePhotos { get; set; } = true;

    partial void OnNameChanged(string value)
    {
        if (!_isSuggestingName)
            _isNameTyped = !string.IsNullOrWhiteSpace(value);
    }

    private void SuggestName()
    {
        _isSuggestingName = true;
        Name = string.IsNullOrWhiteSpace(FolderPath) ? "" : SyncTargets.SuggestedName(FolderPath, _otherTargetNames());
        _isSuggestingName = false;
    }

    [ObservableProperty]
    public partial string? FolderValidationText { get; set; }

    [ObservableProperty]
    public partial bool IsFolderValid { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingFolder { get; set; }

    public TargetSettings Settings => new(
        string.IsNullOrWhiteSpace(Name) ? SyncTargets.SuggestedName(FolderPath, _otherTargetNames()) : Name.Trim(),
        FolderPath.Trim())
    {
        OnlyIphonePhotos = OnlyIphonePhotos,
    };

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (await Pickers.PickFolderAsync() is { } path)
            FolderPath = path;
    }

    // The check starts first: it marks the folder not valid before it awaits, so
    // the name suggested next isn't saved with a folder that hasn't been found.
    partial void OnFolderPathChanged(string value)
    {
        _ = ValidateFolderAsync(value);
        if (!_isNameTyped)
            SuggestName();
    }

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
                : FileActions.IsNetworkFolder(folderPath.Trim()) ? "Folder not found. Check the path, that the NAS or server is on, and that File Explorer can open it."
                : "Folder not found. Check the path, or that its drive is connected or mapped.";
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

    // A backup filed in year folders (Synology Photos', or OneDrive's when it
    // sorts uploads by year and month) is described by them: counting every file
    // in them would read the whole library, over the network for a NAS.
    private static string? DescribeFolderOrNullIfMissing(string folderPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folderPath))
            return null;

        // A folder picked one level up (MobileBackup rather than MobileBackup\iPhone)
        // holds its year folders in a subfolder; the scan reads those too.
        var years = YearsOfYearFolders(folderPath);
        var foldersWithYears = new List<string>();
        if (years.Count == 0)
        {
            foreach (var subfolder in Directory.EnumerateDirectories(folderPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFileName(subfolder) is ['@' or '#' or '.', ..])
                    continue;
                var subfolderYears = YearsOfYearFolders(subfolder);
                years.AddRange(subfolderYears);
                if (subfolderYears.Count > 0)
                    foldersWithYears.Add(Path.GetFileName(subfolder));
            }
        }
        if (years.Count > 0)
        {
            var (first, last) = (years.Min(), years.Max());
            return (first == last ? $"photos from {first}" : $"photos from {first}–{last}")
                + (foldersWithYears.Count > 0 ? $" in {string.Join(", ", foldersWithYears)}" : "");
        }

        int fileCount = 0;
        foreach (var _ in Directory.EnumerateFiles(folderPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileCount++;
        }
        int folderCount = Directory.EnumerateDirectories(folderPath).Count();
        return folderCount == 0 ? $"{fileCount:N0} files" : $"{fileCount:N0} files, {folderCount:N0} folders";
    }

    private static List<int> YearsOfYearFolders(string folderPath) =>
        Directory.EnumerateDirectories(folderPath)
            .Select(Path.GetFileName)
            .Where(name => name is { Length: 4 } && name.All(char.IsAsciiDigit))
            .Select(name => int.Parse(name!, CultureInfo.InvariantCulture))
            .ToList();
}
