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
    private readonly List<Cleanup> _cleanups;
    private int _earliestCoverageYear;

    public LibraryViewModel()
    {
        var cleanups = _cleanups = AppServices.LibraryStore.LoadOrCreateDefault().Cleanups;

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

    public void OpenCleanupAt(DateOnly from, DateOnly to)
    {
        if (_cleanups.FirstOrDefault(c => c.From == from && c.To == to) is { } cleanup)
            Open(cleanup);
    }

    private void StartCleanupFor(LibraryEntry gap) => _ = NewCleanupAsync();

    private void OpenCleanup(LibraryEntry entry)
    {
        if (entry is CleanupEntry { Cleanup: var cleanup })
            Open(cleanup);
    }

    // Reviewing staged files is part of the Review step, the next pull request;
    // every other state reopens the cleanup at its saved pick.
    private void Open(Cleanup cleanup)
    {
        if (cleanup.State == CleanupState.Staged)
        {
            ShellPage.Current?.ShowToast("Reviewing staged photos opens here in the next update.");
            return;
        }
        var targets = AppServices.SettingsStore.LoadOrCreateDefault().Targets;
        ShellPage.Current?.OpenCleanupFlow(new CleanupFlowRequest(cleanup.Id, targets));
    }
}
