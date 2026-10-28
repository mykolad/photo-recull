using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core.Library;

namespace PhotoRecull.App.ViewModels;

public sealed record CoverageSegment(DateOnly From, DateOnly To, LibraryCardStatus Status, string Tooltip);

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    private int _earliestCoverageYear;

    public LibraryViewModel()
    {
        var cleanups = AppServices.LibraryStore.LoadOrCreateDefault().Cleanups;

        foreach (var entry in LibraryTimeline.CleanupsAndGapsNewestFirst(cleanups, _today))
            Cards.Add(entry switch
            {
                NotCleanedGap gap => LibraryCardViewModel.ForGap(gap, _today, StartCleanupFor),
                CleanupEntry cleanup => LibraryCardViewModel.ForCleanup(cleanup.Cleanup, OpenCleanup),
                _ => throw new InvalidOperationException(),
            });

        CoverageSegments = cleanups
            .Select(c => new CoverageSegment(c.From, c.To, LibraryCardViewModel.StatusOf(c.State),
                $"{DateRangeLabel.Format(c.From, c.To)} · {LibraryCardViewModel.StatusLabel(LibraryCardViewModel.StatusOf(c.State))}"))
            .ToList();

        var gaps = LibraryTimeline.FindNotCleanedGaps(cleanups, _today);
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
    private void NewCleanup() => ShowCleanupFlowComingSoon();

    [RelayCommand]
    private void Continue() => ShowCleanupFlowComingSoon();

    public void OpenCleanupAt(DateOnly from, DateOnly to) => ShowCleanupFlowComingSoon();

    private void StartCleanupFor(LibraryEntry gap) => ShowCleanupFlowComingSoon();

    private void OpenCleanup(LibraryEntry cleanup) => ShowCleanupFlowComingSoon();

    // The Pick → Range → Scan → Review flow is the next pull request.
    private static void ShowCleanupFlowComingSoon() =>
        ShellPage.Current?.ShowToast("Cleanups open here in the next update.");
}
