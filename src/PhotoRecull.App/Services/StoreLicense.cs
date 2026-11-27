using PhotoRecull.Core.Library;
using PhotoRecull.Core.Licensing;
#if PHOTORECULL_STORE
using Windows.Services.Store;
#endif

namespace PhotoRecull.App.Services;

public enum PurchaseOutcome
{
    Purchased,
    Cancelled,
    Failed,
}

/// <summary>
/// Whether this copy is bought. Only the Microsoft Store build is paid: it's a paid app with
/// an unlimited free trial, and the trial stages and deletes in one cleanup
/// (<see cref="TrialAllowance"/>). Every other build (dotnet run, a fork, the command-line
/// app) is the open-source app and never limited. Setting PHOTORECULL_SIMULATE_TRIAL=1
/// makes one of those act as a trial, to try the purchase screens without the Store. The
/// Store build ignores it: there, its simulated purchase would unlock the app for free.
/// </summary>
internal static class StoreLicense
{
#if PHOTORECULL_STORE
    public static bool IsLimitedBuild => true;
#else
    public static bool IsLimitedBuild => SimulatesTrial;

    private static readonly bool SimulatesTrial =
        Environment.GetEnvironmentVariable("PHOTORECULL_SIMULATE_TRIAL") == "1";
#endif

    // Until the Store answers, a copy counts as bought: nobody who paid is ever held back by
    // a license that's slow to read, and the purchase gate reads it again before deciding.
    public static bool IsPurchased { get; private set; } = true;

    public static event EventHandler? Changed;

    /// <summary>Whether this copy may stage or delete in a cleanup (null: one not saved yet).</summary>
    public static async Task<bool> MayActAsync(Guid? cleanupId)
    {
        await RefreshAsync();
        return IsPurchased || TrialAllowance.MayAct(cleanupId ?? Guid.Empty, AppServices.Database.LoadCleanups());
    }

    /// <summary>The cleanup the trial's free stage and delete went to, if it's been used.</summary>
    public static Cleanup? FreeCleanup() => TrialAllowance.FreeCleanup(AppServices.Database.LoadCleanups());

    public static async Task RefreshAsync()
    {
        bool wasPurchased = IsPurchased;
#if PHOTORECULL_STORE
        try
        {
            var license = await Context.GetAppLicenseAsync();
            IsPurchased = license.IsActive && !license.IsTrial;
        }
        catch (Exception)
        {
            // The Store can't be asked (its service is broken or missing on this PC). Holding
            // back someone who may have paid is worse than missing one sale.
            IsPurchased = true;
        }
#else
        await Task.CompletedTask;
        IsPurchased = !SimulatesTrial || _simulatedPurchase;
#endif
        if (IsPurchased != wasPurchased)
            Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The app's price in the Store, formatted for this PC's market, or null.</summary>
    public static async Task<string?> PriceAsync()
    {
#if PHOTORECULL_STORE
        try
        {
            var result = await Context.GetStoreProductForCurrentAppAsync();
            return result.ExtendedError is null ? result.Product?.Price?.FormattedPrice : null;
        }
        catch (Exception)
        {
            return null;
        }
#else
        await Task.CompletedTask;
        return SimulatesTrial ? "€9.99 (simulated)" : null;
#endif
    }

    /// <summary>Opens the Store's own purchase window for the app.</summary>
    public static async Task<(PurchaseOutcome Outcome, string? Error)> PurchaseAsync()
    {
#if PHOTORECULL_STORE
        try
        {
            var productResult = await Context.GetStoreProductForCurrentAppAsync();
            if (productResult.Product is not { } product)
                return (PurchaseOutcome.Failed, "The Microsoft Store couldn't be reached. Check your internet connection and try again.");
            var result = await product.RequestPurchaseAsync();
            switch (result.Status)
            {
                case StorePurchaseStatus.Succeeded:
                case StorePurchaseStatus.AlreadyPurchased:
                    await RefreshAsync();
                    return (PurchaseOutcome.Purchased, null);
                case StorePurchaseStatus.NotPurchased:
                    return (PurchaseOutcome.Cancelled, null);
                case StorePurchaseStatus.NetworkError:
                    return (PurchaseOutcome.Failed, "The Microsoft Store couldn't be reached. Check your internet connection and try again.");
                default:
                    return (PurchaseOutcome.Failed, "The Microsoft Store couldn't complete the purchase. Try again, or buy PhotoRecull on its Store page.");
            }
        }
        catch (Exception)
        {
            return (PurchaseOutcome.Failed, "The Microsoft Store couldn't complete the purchase. Try again, or buy PhotoRecull on its Store page.");
        }
#else
        await Task.Delay(500);
        _simulatedPurchase = true;
        await RefreshAsync();
        return (PurchaseOutcome.Purchased, null);
#endif
    }

#if PHOTORECULL_STORE
    private static StoreContext? _context;

    // A desktop app's StoreContext needs the window its purchase dialog belongs to.
    private static StoreContext Context
    {
        get
        {
            if (_context is null)
            {
                _context = StoreContext.GetDefault();
                WinRT.Interop.InitializeWithWindow.Initialize(_context, App.MainWindow.Handle);
                _context.OfflineLicensesChanged += (_, _) =>
                    App.MainWindow.DispatcherQueue.TryEnqueue(() => _ = RefreshAsync());
            }
            return _context;
        }
    }
#else
    private static bool _simulatedPurchase;
#endif
}
