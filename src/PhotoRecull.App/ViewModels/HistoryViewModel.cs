using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public sealed class HistoryViewModel
{
    public HistoryViewModel()
    {
        var rangeOfCleanup = AppServices.Database.LoadCleanups()
            .ToDictionary(c => c.Id, c => DateRangeLabel.Format(c.From, c.To));
        foreach (var run in AppServices.Database.LoadRunsNewestFirst())
            Runs.Add(new HistoryRunViewModel(run, rangeOfCleanup.GetValueOrDefault(run.CleanupId)));
    }

    public ObservableCollection<HistoryRunViewModel> Runs { get; } = [];

    public bool HasNoRuns => Runs.Count == 0;

    // Only the record goes; the files stay wherever the run put them.
    public void Remove(HistoryRunViewModel run)
    {
        AppServices.Database.RemoveRun(run.Run.Id);
        Runs.Remove(run);
    }
}

public sealed partial class HistoryRunViewModel(CleanupRun run, string? cleanupRange)
{
    public CleanupRun Run { get; } = run;

    public string WhenText => Run.RanAt.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    public string Title => $"{(Run.Action == CleanupAction.Staged ? "Staged" : "Deleted")} {PhotoCount(Run.SucceededCount)}"
        + (Run.Action == CleanupAction.Deleted && Run.FreedBytes > 0 ? $" · {FileSizeText.Format(Run.FreedBytes)} freed" : "");

    public string TargetGlyph => ShellPage.TargetGlyph(Run.Target.Kind);

    public string Details => string.Join(" · ", new[]
    {
        SyncTargets.DisplayName(Run.Target.Kind),
        cleanupRange is null ? null : $"Cleanup {cleanupRange}",
        Run.FailedCount > 0 ? $"{PhotoCount(Run.FailedCount)} failed" : null,
    }.OfType<string>());

    public string WhereText => Run.Action == CleanupAction.Staged ? $"Moved to {Run.Destination}"
        : Run.WentToRecycleBin ?? CleanupFlowViewModel.DeletesGoToRecycleBin(Run.Target) ? "Moved to the Recycle Bin"
        : "Deleted on the NAS (to #recycle if DSM's Recycle Bin was on)";

    public bool IsInterrupted => Run.Status == RunStatus.Interrupted;

    public bool CanOpenFolder => Run.Action == CleanupAction.Staged && Run.Destination is not null;

    [RelayCommand]
    private void OpenFolder()
    {
        if (Run.Destination is { } folder && Directory.Exists(folder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        else
            ShellPage.Current?.ShowToast("The stage folder isn't there any more.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
    }

    private static string PhotoCount(int count) => count == 1 ? "1 photo" : $"{count:N0} photos";
}
