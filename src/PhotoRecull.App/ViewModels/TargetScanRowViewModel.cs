using CommunityToolkit.Mvvm.ComponentModel;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public sealed partial class TargetScanRowViewModel(TargetSettings target) : ObservableObject
{
    public TargetSettings Target { get; } = target;

    public string Glyph => ShellPage.TargetGlyph(Target.Kind);

    public string Name => SyncTargets.DisplayName(Target.Kind);

    public string FolderPath => Target.FolderPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial double Percent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsDone { get; set; }

    [ObservableProperty]
    public partial string PhotoFilesFoundText { get; set; } = "0";

    [ObservableProperty]
    public partial string IphonePhotosInRangeText { get; set; } = "–";

    [ObservableProperty]
    public partial string SkippedText { get; set; } = "–";

    [ObservableProperty]
    public partial string CurrentActivityText { get; set; } = "Starting…";

    public string StatusText => IsDone ? "Done" : $"{Percent:0}%";

    public void ShowProgress(ScanProgress progress)
    {
        Percent = progress.FilesTotal == 0 ? 0 : 100.0 * progress.FilesChecked / progress.FilesTotal;
        PhotoFilesFoundText = progress.FilesChecked.ToString("N0");
        CurrentActivityText = $"Reading …\\{Path.GetFileName(progress.CurrentFile)}";
    }

    public void ShowResult(TargetScanResult result)
    {
        Percent = 100;
        IsDone = true;
        PhotoFilesFoundText = result.PhotoFilesFound.ToString("N0");
        IphonePhotosInRangeText = result.IphonePhotosInRange.ToString("N0");
        SkippedText = result.Match.Skipped.Count.ToString("N0");
        CurrentActivityText = "Matched by capture time, one-to-one within ±2 s";
    }
}
