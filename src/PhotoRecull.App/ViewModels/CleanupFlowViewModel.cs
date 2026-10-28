using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public enum CleanupFlowStep
{
    Pick,
    Range,
    Scan,
    Review,
    Confirm,
    Result,
}

public sealed partial class CleanupFlowViewModel : ObservableObject
{
    private readonly DispatcherQueue _uiThread = DispatcherQueue.GetForCurrentThread();
    private readonly AppSettings _settings = AppServices.SettingsStore.LoadOrCreateDefault();
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    private Cleanup? _cleanup;
    private List<DateTimeOffset> _pickedTimestamps = [];
    private CancellationTokenSource? _pickCancellation;
    private CancellationTokenSource? _scanCancellation;
    private string? _pickerUri;

    public CleanupFlowViewModel(CleanupFlowRequest request)
    {
        Targets = request.Targets;
        _cleanup = request.ExistingCleanupId is { } id
            ? AppServices.LibraryStore.LoadOrCreateDefault().Cleanups.FirstOrDefault(c => c.Id == id)
            : null;
        SignInBrowserName = SignInBrowserChoice.DisplayNameFor(_settings.SignInBrowser);
        TargetsLine = string.Join(", ", Targets.Select(t => SyncTargets.DisplayName(t.Kind)));
        IsSavedPickVisible = _cleanup is not null && PickStore.LoadOrNull(_cleanup.Id) is not null;
    }

    public IReadOnlyList<TargetSettings> Targets { get; }

    public string TargetsLine { get; }

    public string SignInBrowserName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPickStep), nameof(IsRangeStep), nameof(IsScanStep))]
    public partial CleanupFlowStep Step { get; set; }

    public bool IsPickStep => Step == CleanupFlowStep.Pick;
    public bool IsRangeStep => Step == CleanupFlowStep.Range;
    public bool IsScanStep => Step == CleanupFlowStep.Scan;

    public string Title => _cleanup is null ? "New cleanup" : DateRangeLabel.Format(_cleanup.From, _cleanup.To);

    public string RangeDaysLine => _cleanup is null
        ? "date range is set after you pick"
        : $"{_cleanup.To.DayNumber - _cleanup.From.DayNumber + 1} days";

    public event EventHandler? BackToLibraryRequested;

    public event EventHandler? TitleChanged;

    // ---------------------------------------------------------------- Pick

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForPickerVisible))]
    public partial bool IsSavedPickVisible { get; set; }

    public bool IsWaitingForPickerVisible => !IsSavedPickVisible;

    [ObservableProperty]
    public partial int SavedPickChoiceIndex { get; set; }

    public string SavedPickTitle => _cleanup is null ? "" : $"Picked {PhotoCount(_cleanup.PickedCount)} on {FormatDay(DateOnly.FromDateTime(_cleanup.PickedAt.LocalDateTime))}";

    public string SavedPickDetail => _cleanup is { EarliestPickedDay: { } earliest, LatestPickedDay: { } latest }
        ? $"Photos from {DateRangeLabel.Format(earliest, latest)} · only capture times are stored"
        : "Only capture times are stored";

    public string UseSavedPickDescription => "Skips the browser. Right when nothing changed in Google Photos for this period since the pick.";

    public string PickAgainDescription => $"Opens Google Photos Picker in {SignInBrowserName} and replaces the saved pick.";

    public string WaitingCaption => $"Waiting for Google Photos Picker in {SignInBrowserName}";

    public string WaitingHeadline => _cleanup is null
        ? "Select every photo you kept in Google Photos for the period you want to clean up, then click Done in the browser."
        : $"Select every photo you kept in Google Photos from {DateRangeLabel.Format(_cleanup.From, _cleanup.To)}, then click Done in the browser.";

    public string WaitingHelp
    {
        get
        {
            if (_cleanup is not null)
                return $"The picker has no date filter. Scroll to {DateRangeLabel.Format(_cleanup.From, _cleanup.To)} and select every photo and video you kept.";
            var previousEnd = CleanupNeighbors.For(AllCleanups(), null).PreviousCleanupEnd;
            return "The picker has no date filter."
                + (previousEnd is { } end ? $" Your last cleanup ends on {FormatDay(end)}, so start from the day after." : "")
                + " PhotoRecull suggests a date range from what you pick.";
        }
    }

    [ObservableProperty]
    public partial string? PickError { get; set; }

    [ObservableProperty]
    public partial bool IsPicking { get; set; }

    [ObservableProperty]
    public partial string? ReauthenticatingMessage { get; set; }

    public void Start()
    {
        if (!IsSavedPickVisible)
            _ = PickInBrowserAsync();
    }

    [RelayCommand]
    private async Task ContinueFromSavedPickAsync()
    {
        if (SavedPickChoiceIndex == 1)
        {
            IsSavedPickVisible = false;
            await PickInBrowserAsync();
            return;
        }
        if (_cleanup is null || PickStore.LoadOrNull(_cleanup.Id) is not { } savedPick)
        {
            IsSavedPickVisible = false;
            PickError = "The saved pick couldn't be read. Pick again.";
            return;
        }
        _pickedTimestamps = savedPick.Timestamps;
        ShowRange(_cleanup.From, _cleanup.To);
    }

    [RelayCommand]
    private void CancelPick()
    {
        _pickCancellation?.Cancel();
        if (_cleanup is not null && PickStore.LoadOrNull(_cleanup.Id) is not null)
        {
            IsSavedPickVisible = true;
            ShellPage.Current?.ShowToast("Pick cancelled. The saved pick is unchanged.");
        }
        else
        {
            ShellPage.Current?.ShowToast("Pick cancelled. Nothing changed.");
            BackToLibraryRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void ReopenBrowser()
    {
        if (_pickerUri is not null)
            BrowserLauncher.Open(_pickerUri, _settings.SignInBrowser);
    }

    [RelayCommand]
    private async Task PickAgainAsync()
    {
        Step = CleanupFlowStep.Pick;
        IsSavedPickVisible = false;
        await PickInBrowserAsync();
    }

    private async Task PickInBrowserAsync()
    {
        _pickCancellation?.Cancel();
        var cancellation = _pickCancellation = new CancellationTokenSource();
        PickError = null;
        ReauthenticatingMessage = null;
        IsPicking = true;

        try
        {
            var picker = await AppServices.Google.CreatePickerClientAsync(_settings.SignInBrowser, _settings.DeveloperCredentialsPath);
            using var http = await picker.AuthorizeAsync(
                onReauthenticatingAfterExpiredToken: () => _uiThread.TryEnqueue(() =>
                    ReauthenticatingMessage = $"Sign-in expired. Re-authenticating in {SignInBrowserName}…"),
                cancellationToken: cancellation.Token);
            ReauthenticatingMessage = null;

            var progress = new Progress<PickerProgress>(p =>
            {
                if (p is PickerProgress.OpenedInBrowser opened)
                    _pickerUri = opened.PickerUri;
            });
            var timestamps = await picker.PickTimestampsAsync(http, progress, cancellation.Token);
            if (timestamps.Count == 0)
            {
                PickError = "No photos were picked. Nothing was saved.";
                return;
            }
            SavePick(timestamps);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (TimeoutException)
        {
            PickError = "Picker timed out. You did not click Done in time. Nothing was saved.";
        }
        catch (Exception ex)
        {
            PickError = $"Couldn't read the pick from Google Photos: {ex.Message}";
        }
        finally
        {
            if (_pickCancellation == cancellation)
                IsPicking = false;
        }
    }

    private void SavePick(List<DateTimeOffset> timestamps)
    {
        _pickedTimestamps = timestamps;
        var pickedDays = timestamps.Select(t => DateRangeFilter.CaptureDay(t, TimeZoneInfo.Local)).ToList();
        var earliest = pickedDays.Min();
        var latest = pickedDays.Max();
        var now = DateTimeOffset.Now;

        var library = AppServices.LibraryStore.LoadOrCreateDefault();
        if (_cleanup is null)
        {
            var neighbors = CleanupNeighbors.For(library.Cleanups, null);
            var suggested = RangeSuggestions.For(earliest, latest, neighbors, _today).FirstOrDefault()
                ?? new RangeSuggestion("", earliest, latest);
            _cleanup = new Cleanup(Guid.NewGuid(), suggested.From, suggested.To, CleanupState.Picked,
                timestamps.Count, now, now);
        }
        _cleanup = _cleanup with
        {
            State = CleanupState.Picked,
            PickedCount = timestamps.Count,
            PickedAt = now,
            LastActivityAt = now,
            EarliestPickedDay = earliest,
            LatestPickedDay = latest,
            ToRemoveCount = null,
        };
        PickStore.Save(_cleanup.Id, new PickCacheData(DateTimeOffset.UtcNow, earliest, latest, timestamps));
        SaveCleanup();
        ShowRange(_cleanup.From, _cleanup.To);
    }

    // ---------------------------------------------------------------- Range

    public ObservableCollection<RangeSuggestionChipViewModel> Suggestions { get; } = [];

    public string PickedSummaryTitle => $"{PhotoCount(_pickedTimestamps.Count)} picked.";

    public string PickedSummaryMessage => _cleanup is { EarliestPickedDay: { } earliest, LatestPickedDay: { } latest }
        ? $"The earliest is from {FormatDay(earliest)}, the latest from {FormatDay(latest)}. Only capture times are saved."
        : "Only capture times are saved.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRangeValid))]
    public partial DateTimeOffset? RangeFrom { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRangeValid))]
    public partial DateTimeOffset? RangeTo { get; set; }

    public bool IsRangeValid => DraftRange() is not null;

    [ObservableProperty]
    public partial string? DaysOutsidePickWarning { get; set; }

    [ObservableProperty]
    public partial string? OverlapWarning { get; set; }

    [ObservableProperty]
    public partial string? PickedPhotosLeftOutNote { get; set; }

    [ObservableProperty]
    public partial string? RangeError { get; set; }

    public DateOnly? EarliestPickedDay => _cleanup?.EarliestPickedDay;

    public DateOnly? LatestPickedDay => _cleanup?.LatestPickedDay;

    public event EventHandler? RangeChanged;

    private void ShowRange(DateOnly from, DateOnly to)
    {
        Suggestions.Clear();
        if (_cleanup is { EarliestPickedDay: { } earliest, LatestPickedDay: { } latest })
        {
            var neighbors = CleanupNeighbors.For(AllCleanups(), _cleanup);
            foreach (var suggestion in RangeSuggestions.For(earliest, latest, neighbors, _today))
                Suggestions.Add(new RangeSuggestionChipViewModel(suggestion, s => SetDraftRange(s.From, s.To)));
        }
        OnPropertyChanged(nameof(PickedSummaryTitle));
        OnPropertyChanged(nameof(PickedSummaryMessage));
        NotifyTitleChanged();
        SetDraftRange(from, to);
        Step = CleanupFlowStep.Range;
    }

    // Both ends change together, so the checks run once on the new range rather
    // than in between, when only one end has moved and the range can look invalid.
    private void SetDraftRange(DateOnly from, DateOnly to)
    {
        _isSettingBothRangeEnds = true;
        RangeFrom = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue));
        RangeTo = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue));
        _isSettingBothRangeEnds = false;
        UpdateRangeChecks();
    }

    private bool _isSettingBothRangeEnds;

    partial void OnRangeFromChanged(DateTimeOffset? value)
    {
        if (!_isSettingBothRangeEnds)
            UpdateRangeChecks();
    }

    partial void OnRangeToChanged(DateTimeOffset? value)
    {
        if (!_isSettingBothRangeEnds)
            UpdateRangeChecks();
    }

    public (DateOnly From, DateOnly To)? DraftRange()
    {
        if (RangeFrom is not { } from || RangeTo is not { } to)
            return null;
        var (fromDay, toDay) = (DateOnly.FromDateTime(from.Date), DateOnly.FromDateTime(to.Date));
        return fromDay <= toDay ? (fromDay, toDay) : null;
    }

    private void UpdateRangeChecks()
    {
        var draft = DraftRange();
        RangeError = draft is null ? "Choose a start date on or before the end date." : null;
        foreach (var chip in Suggestions)
            chip.IsSelected = draft is { } d && chip.Suggestion.From == d.From && chip.Suggestion.To == d.To;

        DaysOutsidePickWarning = OverlapWarning = PickedPhotosLeftOutNote = null;
        if (draft is { } range && _cleanup is { EarliestPickedDay: { } earliest, LatestPickedDay: { } latest })
        {
            var daysOutside = RangeChecks.DaysOutsideThePick(range.From, range.To, earliest, latest);
            if (daysOutside.Count > 0)
                DaysOutsidePickWarning =
                    $"Your pick has no photos {string.Join(" or ", daysOutside.Select(DescribeDays))}. " +
                    "Anything on your targets from those days will be flagged for removal. " +
                    "If you kept photos from then, pick again and include them.";

            var overlapping = RangeChecks.OverlappingCleanups(AllCleanups(), _cleanup.Id, range.From, range.To);
            if (overlapping.Count > 0)
                OverlapWarning =
                    $"Overlaps your {string.Join(", ", overlapping.Select(c => DateRangeLabel.Format(c.From, c.To)))} cleanup. " +
                    "Photos already handled there are scanned again.";

            if (RangeChecks.LeavesSomePickedPhotosOutside(range.From, range.To, earliest, latest))
                PickedPhotosLeftOutNote =
                    "Some picked photos are outside this range. They are ignored, and files from those days are not touched.";
        }
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- Scan

    public ObservableCollection<TargetScanRowViewModel> ScanRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanTitle))]
    public partial bool IsScanComplete { get; set; }

    [ObservableProperty]
    public partial string? ScanError { get; set; }

    public string ScanTitle => IsScanComplete
        ? "Scan complete"
        : $"Scanning {Targets.Count} target{(Targets.Count == 1 ? "" : "s")}…";

    public IReadOnlyList<TargetScanResult> ScanResults { get; private set; } = [];

    [RelayCommand]
    private async Task ScanTargetsAsync()
    {
        if (_cleanup is null || DraftRange() is not { } range)
            return;

        _cleanup = _cleanup with { From = range.From, To = range.To, LastActivityAt = DateTimeOffset.Now };
        SaveCleanup();
        NotifyTitleChanged();

        _scanCancellation?.Cancel();
        var cancellation = _scanCancellation = new CancellationTokenSource();
        ScanRows.Clear();
        foreach (var target in Targets)
            ScanRows.Add(new TargetScanRowViewModel(target));
        IsScanComplete = false;
        ScanError = null;
        Step = CleanupFlowStep.Scan;

        try
        {
            var picked = _pickedTimestamps;
            ScanResults = await Task.WhenAll(ScanRows.Select(row =>
            {
                var progress = new ThrottledProgress<ScanProgress>(
                    new Progress<ScanProgress>(row.ShowProgress), TimeSpan.FromMilliseconds(100));
                return Task.Run(() =>
                {
                    var result = CleanupScanner.ScanAndMatch(SyncTargets.Create(row.Target), picked,
                        range.From, range.To, TimeZoneInfo.Local, progress, cancellation.Token);
                    _uiThread.TryEnqueue(() => row.ShowResult(result));
                    return result;
                }, cancellation.Token);
            }));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            ScanError = $"Couldn't scan: {ex.Message}";
            return;
        }

        _cleanup = _cleanup with
        {
            State = CleanupState.ReadyToAct,
            ToRemoveCount = ScanResults.Sum(r => r.Match.Unmatched.Count),
            LastActivityAt = DateTimeOffset.Now,
        };
        SaveCleanup();
        IsScanComplete = true;
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCancellation?.Cancel();
        Step = CleanupFlowStep.Range;
    }

    // The Review step is the next pull request.
    [RelayCommand]
    private void ReviewResults() =>
        ShellPage.Current?.ShowToast($"{PhotoCount(_cleanup?.ToRemoveCount ?? 0)} to review. Review opens here in the next update.");

    [RelayCommand]
    private void BackToLibrary()
    {
        CancelRunningWork();
        BackToLibraryRequested?.Invoke(this, EventArgs.Empty);
    }

    public void CancelRunningWork()
    {
        _pickCancellation?.Cancel();
        _scanCancellation?.Cancel();
    }

    // ---------------------------------------------------------------- helpers

    private void NotifyTitleChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(RangeDaysLine));
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveCleanup()
    {
        var library = AppServices.LibraryStore.LoadOrCreateDefault();
        var cleanups = library.Cleanups.Where(c => c.Id != _cleanup!.Id).Append(_cleanup!).OrderBy(c => c.From).ToList();
        AppServices.LibraryStore.Save(library with { Cleanups = cleanups });
    }

    private static List<Cleanup> AllCleanups() => AppServices.LibraryStore.LoadOrCreateDefault().Cleanups;

    private static string FormatDay(DateOnly day) => day.ToString("d MMM yyyy");

    private static string PhotoCount(int count) => count == 1 ? "1 photo" : $"{count:N0} photos";

    private static string DescribeDays(DayRange days) => days.From == days.To
        ? $"on {FormatDay(days.From)}"
        : $"from {days.From:d MMM} to {FormatDay(days.To)}";
}
