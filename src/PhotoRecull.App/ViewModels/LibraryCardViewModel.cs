using CommunityToolkit.Mvvm.Input;
using PhotoRecull.Core;
using PhotoRecull.Core.Library;

namespace PhotoRecull.App.ViewModels;

public enum LibraryCardStatus
{
    NotCleaned,
    Picked,
    ReadyToAct,
    Staged,
    Done,
}

public sealed class LibraryCardViewModel
{
    private LibraryCardViewModel(
        LibraryEntry entry, string rangeLabel, LibraryCardStatus status,
        string? pickedLine, string stateLine, string actionText, IRelayCommand actionCommand)
    {
        Entry = entry;
        RangeLabel = rangeLabel;
        Status = status;
        PickedLine = pickedLine;
        StateLine = stateLine;
        ActionText = actionText;
        ActionCommand = actionCommand;
    }

    public LibraryEntry Entry { get; }
    public string RangeLabel { get; }
    public string DaysLabel => Entry.DayCount == 1 ? "1 day" : $"{Entry.DayCount} days";
    public LibraryCardStatus Status { get; }
    public string StatusText => StatusLabel(Status);
    public string? PickedLine { get; }
    public string StateLine { get; }
    public string ActionText { get; }
    public IRelayCommand ActionCommand { get; }
    public bool IsNotCleanedGap => Entry is NotCleanedGap;
    public bool IsReadyToAct => Status == LibraryCardStatus.ReadyToAct;
    public Cleanup? Cleanup => (Entry as CleanupEntry)?.Cleanup;
    public bool CanBeDiscarded => Cleanup?.CanBeDiscarded == true;

    public static string StatusLabel(LibraryCardStatus status) => status switch
    {
        LibraryCardStatus.NotCleaned => "Not cleaned",
        LibraryCardStatus.Picked => "Picked",
        LibraryCardStatus.ReadyToAct => "Ready to act",
        LibraryCardStatus.Staged => "Staged",
        _ => "Done",
    };

    public static LibraryCardStatus StatusOf(CleanupState state) => state switch
    {
        CleanupState.Picked => LibraryCardStatus.Picked,
        CleanupState.ReadyToAct => LibraryCardStatus.ReadyToAct,
        CleanupState.Staged => LibraryCardStatus.Staged,
        _ => LibraryCardStatus.Done,
    };

    public static LibraryCardViewModel ForGap(NotCleanedGap gap, DateOnly today, Action<LibraryEntry> startCleanup) =>
        new(gap,
            gap.To == today ? DateRangeLabel.FormatUntilToday(gap.From) : DateRangeLabel.Format(gap.From, gap.To),
            LibraryCardStatus.NotCleaned,
            pickedLine: null,
            stateLine: "Not cleaned up yet",
            actionText: "New cleanup",
            new RelayCommand(() => startCleanup(gap)));

    public static LibraryCardViewModel ForCleanup(Cleanup cleanup, Action<LibraryEntry> openCleanup)
    {
        var entry = new CleanupEntry(cleanup);
        var (stateLine, actionText) = cleanup.State switch
        {
            CleanupState.Picked => ("Not scanned yet", "Continue"),
            CleanupState.ReadyToAct => ($"{cleanup.ToRemoveCount ?? 0:N0} to remove", "Continue review"),
            CleanupState.Staged => ($"{cleanup.StagedCount:N0} staged, waiting to delete", "Review staged"),
            _ => ($"{cleanup.RemovedCount:N0} removed · {FileSizeText.Format(cleanup.FreedBytes)} freed", "Run again"),
        };
        return new(entry,
            DateRangeLabel.Format(cleanup.From, cleanup.To),
            StatusOf(cleanup.State),
            $"{cleanup.PickedCount:N0} photos picked",
            stateLine,
            actionText,
            new RelayCommand(() => openCleanup(entry)));
    }
}
