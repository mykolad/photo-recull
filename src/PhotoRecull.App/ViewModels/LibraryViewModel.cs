using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public sealed record CoverageSegment(Guid CleanupId, DateOnly From, DateOnly To, LibraryCardStatus Status, string Tooltip);

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    private readonly List<Cleanup> _cleanups;
    private int _earliestCoverageYear;

    public LibraryViewModel()
    {
        var cleanups = _cleanups = AppServices.Database.LoadCleanups();

        foreach (var entry in LibraryTimeline.CleanupsAndGapsNewestFirst(cleanups, _today))
            Cards.Add(entry switch
            {
                NotCleanedGap gap => LibraryCardViewModel.ForGap(gap, _today, StartCleanupFor),
                CleanupEntry cleanup => LibraryCardViewModel.ForCleanup(cleanup.Cleanup, OpenCleanup),
                _ => throw new InvalidOperationException(),
            });

        CoverageSegments = cleanups
            .Select(c => new CoverageSegment(c.Id, c.From, c.To, LibraryCardViewModel.StatusOf(c.State),
                $"{DateRangeLabel.Format(c.From, c.To)} · {LibraryCardViewModel.StatusLabel(LibraryCardViewModel.StatusOf(c.State))}"))
            .ToList();

        var gaps = NotCleanedGaps = LibraryTimeline.FindNotCleanedGaps(cleanups, _today);
        NotCleanedNote = gaps.Count == 0
            ? (cleanups.Count == 0 ? "No cleanups yet" : "Everything is cleaned up")
            : "Not cleaned up yet: " + string.Join(", ", gaps.Select(g =>
                g.To == _today ? DateRangeLabel.FormatUntilToday(g.From) : DateRangeLabel.Format(g.From, g.To)));

        _earliestCoverageYear = cleanups.Count == 0 ? _today.Year : cleanups.Min(c => c.From.Year);
        CoverageYear = _today.Year;

        ContinueCleanup = cleanups
            .Where(c => c.State == CleanupState.ReadyToAct)
            .OrderByDescending(c => c.LastActivityAt)
            .FirstOrDefault();
    }

    public ObservableCollection<LibraryCardViewModel> Cards { get; } = [];

    public IReadOnlyList<CoverageSegment> CoverageSegments { get; }

    public IReadOnlyList<NotCleanedGap> NotCleanedGaps { get; }

    public DateOnly Today => _today;

    public string NotCleanedNote { get; }

    public bool HasNoCleanups => CoverageSegments.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowPreviousYearCommand), nameof(ShowNextYearCommand))]
    public partial int CoverageYear { get; set; }

    private Cleanup? ContinueCleanup { get; }

    public bool IsContinueVisible => ContinueCleanup is not null;

    public string ContinueText => ContinueCleanup is { } c ? $"Continue {DateRangeLabel.Format(c.From, c.To)}" : "";

    [RelayCommand(CanExecute = nameof(CanShowPreviousYear))]
    private void ShowPreviousYear() => CoverageYear--;

    private bool CanShowPreviousYear() => CoverageYear > _earliestCoverageYear;

    [RelayCommand(CanExecute = nameof(CanShowNextYear))]
    private void ShowNextYear() => CoverageYear++;

    private bool CanShowNextYear() => CoverageYear < _today.Year;

    [RelayCommand]
    private Task NewCleanupAsync() => ShellPage.Current?.StartNewCleanupAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private void Continue()
    {
        if (ContinueCleanup is { } cleanup)
            Open(cleanup);
    }

    public void OpenCleanup(Guid cleanupId)
    {
        if (_cleanups.FirstOrDefault(c => c.Id == cleanupId) is { } cleanup)
            Open(cleanup);
    }

    // Library is rebuilt afterwards: the cards, Coverage and the gaps all change.
    public void Discard(Cleanup cleanup)
    {
        var label = DateRangeLabel.Format(cleanup.From, cleanup.To);
        bool discarded = AppServices.Database.DiscardCleanup(cleanup.Id);
        ShellPage.Current?.ShowLibrary();
        if (discarded)
            ShellPage.Current?.ShowToast($"Discarded {label}. No files were moved or deleted.");
        else
            ShellPage.Current?.ShowToast($"{label} can't be discarded: it has moved or deleted files.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
    }

    private void StartCleanupFor(LibraryEntry gap) => _ = NewCleanupAsync();

    private void OpenCleanup(LibraryEntry entry)
    {
        if (entry is CleanupEntry { Cleanup: var cleanup })
            Open(cleanup);
    }

    // A cleanup repeats the cull on the targets chosen when it started, as long as
    // they're still configured; cleanups saved before targets were recorded (or
    // whose targets have all been removed since) use the current ones.
    private static List<TargetSettings> TargetsToReopen(Cleanup cleanup)
    {
        var configured = AppServices.Database.LoadSettings().Targets;
        var stillConfigured = cleanup.Targets?.Where(configured.Contains).ToList() ?? [];
        return stillConfigured.Count > 0 ? stillConfigured : configured;
    }

    // A Staged cleanup opens its staged photos; every other state reopens the
    // cleanup at its saved pick.
    private void Open(Cleanup cleanup)
    {
        if (cleanup.State == CleanupState.Staged)
        {
            ShellPage.Current?.OpenStagedReview(cleanup.Id);
            return;
        }
        ShellPage.Current?.OpenCleanupFlow(new CleanupFlowRequest(cleanup.Id, TargetsToReopen(cleanup)));
    }
}
