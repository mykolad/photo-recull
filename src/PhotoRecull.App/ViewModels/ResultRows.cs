using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace PhotoRecull.App.ViewModels;

public sealed partial class ResultTargetRow(
    string glyph, string name, string location, string succeededLabel, int succeededCount, int failedCount,
    string? folderToOpen, string? openButtonText)
{
    public string Glyph { get; } = glyph;
    public string Name { get; } = name;
    public string Location { get; } = location;
    public string SucceededLabel { get; } = succeededLabel;
    public int SucceededCount { get; } = succeededCount;
    public int FailedCount { get; } = failedCount;
    public string? OpenButtonText { get; } = openButtonText;
    public bool HasOpenButton => OpenButtonText is not null;

    [RelayCommand]
    private void Open()
    {
        // "shell:RecycleBinFolder" opens the Recycle Bin in File Explorer.
        var target = folderToOpen ?? "shell:RecycleBinFolder";
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
    }
}

public sealed record ResultFailureRow(string FileName, string TargetName, string Reason);
