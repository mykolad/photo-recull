using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
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
    private readonly AppSettings _settings = AppServices.Database.LoadSettings();
    // Read each time: the app can stay open past midnight.
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private Cleanup? _cleanup;
    private List<DateTimeOffset> _pickedTimestamps = [];
    private CancellationTokenSource? _pickCancellation;
    private CancellationTokenSource? _scanCancellation;
    private string? _pickerUri;

    public CleanupFlowViewModel(CleanupFlowRequest request)
    {
        Targets = request.Targets;
        _cleanup = request.ExistingCleanupId is { } id
            ? AppServices.Database.LoadCleanups().FirstOrDefault(c => c.Id == id)
            : null;
        SignInBrowserName = SignInBrowserChoice.DisplayNameFor(_settings.SignInBrowser);
        TargetsLine = string.Join(", ", Targets.Select(t => SyncTargets.DisplayName(t.Kind)));
        IsSavedPickVisible = _cleanup is not null && AppServices.Database.LoadPick(_cleanup.Id) is not null;
    }

    public IReadOnlyList<TargetSettings> Targets { get; }

    public string TargetsLine { get; }

    public string SignInBrowserName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPickStep), nameof(IsRangeStep), nameof(IsScanStep), nameof(IsReviewStep), nameof(IsResultStep))]
    public partial CleanupFlowStep Step { get; set; }

    public bool IsPickStep => Step == CleanupFlowStep.Pick;
    public bool IsRangeStep => Step == CleanupFlowStep.Range;
    public bool IsScanStep => Step == CleanupFlowStep.Scan;
    public bool IsReviewStep => Step is CleanupFlowStep.Review or CleanupFlowStep.Confirm;
    public bool IsResultStep => Step == CleanupFlowStep.Result;

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

    // A cleanup that was scanned before ("Continue review") is scanned again with
    // its saved pick, since files may have changed, and opens straight in Review.
    public void Start()
    {
        if (_cleanup is { State: CleanupState.ReadyToAct } cleanup && AppServices.Database.LoadPick(cleanup.Id) is { } savedPick)
        {
            _pickedTimestamps = savedPick.Timestamps;
            SetDraftRange(cleanup.From, cleanup.To);
            _ = ScanThenReviewAsync();
            return;
        }
        if (!IsSavedPickVisible)
            _ = PickInBrowserAsync();
    }

    private async Task ScanThenReviewAsync()
    {
        await ScanTargetsAsync();
        if (IsScanComplete)
            ShowReview();
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
        if (_cleanup is null || AppServices.Database.LoadPick(_cleanup.Id) is not { } savedPick)
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
        if (_cleanup is not null && AppServices.Database.LoadPick(_cleanup.Id) is not null)
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

    [RelayCommand(CanExecute = nameof(CanPickAgain))]
    private async Task PickAgainAsync()
    {
        Step = CleanupFlowStep.Pick;
        IsSavedPickVisible = false;
        await PickInBrowserAsync();
    }

    private bool CanPickAgain() => !IsActing;

    private bool CanLeave() => !IsActing;

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
            // A cancel can land after the picker's last response but before this
            // continuation runs; it must not still replace the saved pick.
            if (cancellation.IsCancellationRequested)
                return;
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

    // The pick and the cleanup details that describe it are saved in one
    // transaction, and only then replace what this flow shows.
    private void SavePick(List<DateTimeOffset> timestamps)
    {
        var pickedDays = timestamps.Select(t => DateRangeFilter.CaptureDay(t, TimeZoneInfo.Local)).ToList();
        var earliest = pickedDays.Min();
        var latest = pickedDays.Max();
        var now = DateTimeOffset.Now;

        var pickedCleanup = _cleanup;
        if (pickedCleanup is null)
        {
            var neighbors = CleanupNeighbors.For(AllCleanups(), null);
            var suggested = RangeSuggestions.For(earliest, latest, neighbors, Today).FirstOrDefault()
                ?? new RangeSuggestion("", earliest, latest);
            pickedCleanup = new Cleanup(Guid.NewGuid(), suggested.From, suggested.To, CleanupState.Picked,
                timestamps.Count, now, now) { Targets = Targets.ToList() };
        }
        pickedCleanup = pickedCleanup with
        {
            State = CleanupState.Picked,
            PickedCount = timestamps.Count,
            PickedAt = now,
            LastActivityAt = now,
            EarliestPickedDay = earliest,
            LatestPickedDay = latest,
            ToRemoveCount = null,
            KeptByUserCount = 0,
        };

        try
        {
            AppServices.Database.SavePickWithCleanup(pickedCleanup, timestamps);
        }
        catch (SqliteException ex)
        {
            PickError = $"Couldn't save the pick: {ex.Message}";
            return;
        }
        _cleanup = pickedCleanup;
        _pickedTimestamps = timestamps;
        OnPropertyChanged(nameof(SavedPickTitle));
        OnPropertyChanged(nameof(SavedPickDetail));
        OnPropertyChanged(nameof(WaitingHeadline));
        OnPropertyChanged(nameof(WaitingHelp));
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
            foreach (var suggestion in RangeSuggestions.For(earliest, latest, neighbors, Today))
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

        // Until this scan succeeds, results from an earlier scan (maybe of other
        // dates) no longer describe the cleanup.
        _cleanup = _cleanup with
        {
            From = range.From,
            To = range.To,
            State = CleanupState.Picked,
            ToRemoveCount = null,
            LastActivityAt = DateTimeOffset.Now,
        };
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

        // Cancellation is only checked while files are read, so a cancel after the
        // last file can still let the scans finish; don't record that as done.
        if (cancellation.IsCancellationRequested)
            return;

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

    [RelayCommand]
    private void ReviewResults() => ShowReview();

    // ---------------------------------------------------------------- Review, Confirm, Result

    [ObservableProperty]
    public partial ReviewViewModel? Review { get; set; }

    private void ShowReview()
    {
        if (_cleanup is null)
            return;
        Review = new ReviewViewModel(_cleanup, ScanResults, Targets);
        Step = CleanupFlowStep.Review;
    }

    public void BeginConfirm() => Step = CleanupFlowStep.Confirm;

    public void CancelConfirm() => Step = CleanupFlowStep.Review;

    public string? DefaultStageFolderFor(TargetSettings target) =>
        SyncTargets.Create(target).DefaultStageFolder(_cleanup!.From, _cleanup.To);

    public IReadOnlyList<(TargetSettings Target, int Count, long Bytes)> CandidatesPerTarget() =>
        Review?.CandidatesToActOn
            .GroupBy(c => c.Target)
            .Select(g => (g.Key, g.Count(), g.Sum(c => c.Photo.SizeBytes)))
            .ToList() ?? [];

    private CleanupAction _lastAction;
    private Dictionary<TargetSettings, string> _lastStageFolders = [];
    private List<(TargetSettings Target, FileFailure Failure)> _lastFailures = [];

    public Task StageAsync(IReadOnlyDictionary<TargetSettings, string> stageFolders) =>
        ActOnAsync(CleanupAction.Staged, stageFolders,
            Review!.CandidatesToActOn.Select(t => (t.Target, t.Photo)).ToList());

    public Task DeleteAsync() =>
        ActOnAsync(CleanupAction.Deleted, new Dictionary<TargetSettings, string>(),
            Review!.CandidatesToActOn.Select(t => (t.Target, t.Photo)).ToList());

    [RelayCommand]
    private Task RetryFailedAsync() =>
        ActOnAsync(_lastAction, _lastStageFolders, _lastFailures.Select(f => (f.Target, f.Failure.Photo)).ToList());

    // The review stays locked until the result shows, including while the NAS is
    // asked about its Recycle Bin, so no second run can start on the same photos.
    private async Task ActOnAsync(
        CleanupAction action, IReadOnlyDictionary<TargetSettings, string> stageFolders,
        List<(TargetSettings Target, PhotoFile Photo)> photos)
    {
        IsActing = true;
        try
        {
            await ActAndShowResultAsync(action, stageFolders, photos);
        }
        finally
        {
            IsActing = false;
        }
    }

    private async Task ActAndShowResultAsync(
        CleanupAction action, IReadOnlyDictionary<TargetSettings, string> stageFolders,
        List<(TargetSettings Target, PhotoFile Photo)> photos)
    {
        var ranAt = DateTimeOffset.Now;
        var perTarget = await Task.Run(() => photos
            .GroupBy(p => p.Target)
            .Select(g =>
            {
                var targetPhotos = g.Select(p => p.Photo).ToList();
                var result = action == CleanupAction.Staged
                    ? FileActions.MoveToStageFolder(targetPhotos, stageFolders[g.Key], g.Key.FolderPath)
                    : DeleteFrom(g.Key, targetPhotos);
                return (Target: g.Key, Result: result);
            })
            .ToList());

        var runs = perTarget.Select(r => new CleanupRun(_cleanup!.Id, ranAt, action, r.Target,
            action == CleanupAction.Staged ? stageFolders[r.Target] : null,
            r.Result.SucceededCount, r.Result.Failures.Count,
            action == CleanupAction.Deleted ? r.Result.SucceededBytes : 0)).ToList();
        int succeeded = runs.Sum(r => r.SucceededCount);

        int failed = runs.Sum(r => r.FailedCount);
        // A Live Photo whose video went before its photo failed frees space
        // without completing a photo; that space still counts.
        if (succeeded > 0 || runs.Sum(r => r.FreedBytes) > 0)
        {
            // Only a complete run moves the cleanup on: with failures left it stays
            // Ready to act, so Library can reopen it and the rest can be retried.
            _cleanup = _cleanup! with
            {
                State = failed > 0 ? CleanupState.ReadyToAct
                    : action == CleanupAction.Staged ? CleanupState.Staged : CleanupState.Done,
                ToRemoveCount = failed > 0 ? failed : _cleanup.ToRemoveCount,
                StagedCount = action == CleanupAction.Staged ? _cleanup.StagedCount + succeeded : _cleanup.StagedCount,
                RemovedCount = action == CleanupAction.Deleted ? _cleanup.RemovedCount + succeeded : _cleanup.RemovedCount,
                FreedBytes = _cleanup.FreedBytes + runs.Sum(r => r.FreedBytes),
                KeptByUserCount = Review?.KeptByUserCount ?? _cleanup.KeptByUserCount,
                LastActivityAt = ranAt,
            };
        }
        AppServices.Database.RecordRunsWithCleanup(_cleanup!, runs);

        _lastAction = action;
        _lastStageFolders = stageFolders.ToDictionary();
        _lastFailures = perTarget.SelectMany(r => r.Result.Failures.Select(f => (r.Target, f))).ToList();
        var dsmRecycleBins = action == CleanupAction.Deleted
            ? await DsmRecycleBinFoldersAsync(perTarget.Select(r => r.Target).ToList())
            : new Dictionary<TargetSettings, string?>();
        ShowResult(action, perTarget, stageFolders, dsmRecycleBins);
    }

    private static FileActionResult DeleteFrom(TargetSettings target, List<PhotoFile> photos) =>
        DeletesGoToRecycleBin(target)
            ? FileActions.MoveToRecycleBin(photos)
            : FileActions.DeletePermanently(photos);

    public static bool DeletesGoToRecycleBin(TargetSettings target) => target.Kind == TargetKind.OneDrive;

    // Synology photos are deleted over the network, past the Windows Recycle
    // Bin; they can only be restored if DSM's Recycle Bin is on for the share.
    // A #recycle folder there shows it was turned on once, not that it still
    // is. The NAS is asked off the UI thread, and given up on after a few
    // seconds: one that dropped off the network would otherwise freeze the app
    // until the SMB request times out.
    public static async Task<IReadOnlyDictionary<TargetSettings, string?>> DsmRecycleBinFoldersAsync(
        IReadOnlyCollection<TargetSettings> targets)
    {
        var probe = Task.Run(() => targets.ToDictionary(t => t, DsmRecycleBinFolder));
        return await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(3))) == probe
            ? await probe
            : targets.ToDictionary(t => t, _ => (string?)null);
    }

    // Before the Delete dialog opens: the review is locked as during a run, so
    // nothing can be staged, deleted or left while the NAS answers.
    public async Task<IReadOnlyDictionary<TargetSettings, string?>> CheckDsmRecycleBinsBeforeDeleteAsync()
    {
        IsActing = true;
        try
        {
            return await DsmRecycleBinFoldersAsync(CandidatesPerTarget().Select(t => t.Target).ToList());
        }
        finally
        {
            IsActing = false;
        }
    }

    private static string? DsmRecycleBinFolder(TargetSettings target) =>
        SyncTargets.Create(target) is SynologyPhotosTarget { HasRecycleBin: true } synology ? synology.RecycleBinFolder : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PickAgainCommand), nameof(BackToLibraryCommand), nameof(ViewHistoryCommand))]
    public partial bool IsActing { get; set; }

    // Leaving mid-run would let the same cleanup be reopened and scanned or acted
    // on again while files are still moving, with two screens saving its state.
    partial void OnIsActingChanged(bool value)
    {
        if (Review is not null)
            Review.IsActionRunning = value;
        ShellPage.Current?.SetNavigationLocked(value);
    }

    public ObservableCollection<ResultTargetRow> ResultTargets { get; } = [];

    public ObservableCollection<ResultFailureRow> ResultFailures { get; } = [];

    [ObservableProperty]
    public partial string ResultTitle { get; set; } = "";

    [ObservableProperty]
    public partial string ResultMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool ResultHasFailures { get; set; }

    private void ShowResult(
        CleanupAction action,
        List<(TargetSettings Target, FileActionResult Result)> perTarget,
        IReadOnlyDictionary<TargetSettings, string> stageFolders,
        IReadOnlyDictionary<TargetSettings, string?> dsmRecycleBins)
    {
        int succeeded = perTarget.Sum(r => r.Result.SucceededCount);
        int failed = perTarget.Sum(r => r.Result.Failures.Count);
        var verb = action == CleanupAction.Staged ? "Staged" : "Deleted";
        ResultTitle = $"{verb} {FileCount(succeeded)}.";
        ResultMessage = failed > 0
            ? $"{FileCount(failed)} failed. {Title} stays Ready to act in Library, so you can retry them now or later."
            : $"{Title} is now marked {(action == CleanupAction.Staged ? "Staged" : "Done")} in Library.";
        ResultHasFailures = failed > 0;

        ResultTargets.Clear();
        foreach (var (target, result) in perTarget)
        {
            var dsmRecycleBin = dsmRecycleBins.GetValueOrDefault(target);
            var (location, folderToOpen, openButtonText) = action == CleanupAction.Staged
                ? (stageFolders[target], stageFolders[target], "Open folder")
                : DeletesGoToRecycleBin(target) ? ("Moved to the Recycle Bin", null, "Recycle Bin")
                : dsmRecycleBin is not null ? ("Deleted. If DSM's Recycle Bin is on, they're in #recycle on the NAS", dsmRecycleBin, "Open #recycle")
                : ("Deleted permanently, unless DSM's Recycle Bin is on", (string?)null, (string?)null);
            ResultTargets.Add(new ResultTargetRow(
                ShellPage.TargetGlyph(target.Kind), SyncTargets.DisplayName(target.Kind), location,
                action == CleanupAction.Staged ? "Moved" : "Deleted", result.SucceededCount, result.Failures.Count,
                folderToOpen, openButtonText));
        }
        ResultFailures.Clear();
        foreach (var (target, failure) in _lastFailures)
            ResultFailures.Add(new ResultFailureRow(failure.Photo.FileName, SyncTargets.DisplayName(target.Kind), failure.Reason));

        NotifyTitleChanged();
        Step = CleanupFlowStep.Result;
    }

    private static string FileCount(int count) => count == 1 ? "1 file" : $"{count:N0} files";

    [RelayCommand(CanExecute = nameof(CanLeave))]
    private void ViewHistory() => ShellPage.Current?.ShowHistory();

    // A scan with nothing to remove, or a review where every candidate was kept,
    // still finishes the cleanup; nothing is moved or deleted.
    [RelayCommand]
    private void MarkAsDone()
    {
        if (_cleanup is null)
            return;
        _cleanup = _cleanup with
        {
            State = CleanupState.Done,
            ToRemoveCount = 0,
            KeptByUserCount = Review?.KeptByUserCount ?? _cleanup.KeptByUserCount,
            LastActivityAt = DateTimeOffset.Now,
        };
        SaveCleanup();
        ResultTitle = "Nothing to remove.";
        ResultMessage = $"{Title} is now marked Done in Library. No files were moved or deleted.";
        ResultHasFailures = false;
        ResultTargets.Clear();
        ResultFailures.Clear();
        _lastFailures = [];
        NotifyTitleChanged();
        Step = CleanupFlowStep.Result;
    }

    [RelayCommand(CanExecute = nameof(CanLeave))]
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

    private void SaveCleanup() => AppServices.Database.SaveCleanup(_cleanup!);

    private static List<Cleanup> AllCleanups() => AppServices.Database.LoadCleanups();

    private static string FormatDay(DateOnly day) => day.ToString("d MMM yyyy");

    private static string PhotoCount(int count) => count == 1 ? "1 photo" : $"{count:N0} photos";

    private static string DescribeDays(DayRange days) => days.From == days.To
        ? $"on {FormatDay(days.From)}"
        : $"from {days.From:d MMM} to {FormatDay(days.To)}";
}
