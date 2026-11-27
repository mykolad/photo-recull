using Windows.Services.Store;

namespace PhotoRecull.App.Services;

// Compiled into the Microsoft Store build only (PhotoRecull.App.csproj); every other build
// has OpenSourceLicenseSource instead.
internal static partial class StoreLicense
{
    private static partial ILicenseSource CreateSource() => new StoreLicenseSource();
}

/// <summary>
/// The Store build's license: the Microsoft Store's, for a paid app with an unlimited free
/// trial. Bought means active and not a trial. PHOTORECULL_SIMULATE_TRIAL only makes the copy
/// act as unbought, to test the trial on a PC that owns the app or with a package registered
/// from the build output; price and Buy stay the Store's own, so it can't unlock anything.
/// </summary>
internal sealed class StoreLicenseSource : ILicenseSource
{
    private const string StoreUnreachable =
        "The Microsoft Store couldn't be reached. Check your internet connection and try again.";

    private const string PurchaseFailed =
        "The Microsoft Store couldn't complete the purchase. Try again, or buy PhotoRecull on its Store page.";

    private StoreContext? _context;

    public bool IsLimited => true;

    public async Task<bool> IsPurchasedAsync()
    {
        bool isPurchased;
        try
        {
            var license = await Context.GetAppLicenseAsync();
            isPurchased = license.IsActive && !license.IsTrial;
        }
        catch (Exception)
        {
            // The Store can't be asked (its service is broken or missing on this PC). Holding
            // back someone who may have paid is worse than missing one sale.
            isPurchased = true;
        }
        // Only ever takes a purchase away, so setting it can't unlock anything.
        return isPurchased && !StoreLicense.SimulatesTrial;
    }

    public async Task<string?> PriceAsync()
    {
        try
        {
            var result = await Context.GetStoreProductForCurrentAppAsync();
            return result.ExtendedError is null ? result.Product?.Price?.FormattedPrice : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<(PurchaseOutcome Outcome, string? Error)> PurchaseAsync()
    {
        try
        {
            var productResult = await Context.GetStoreProductForCurrentAppAsync();
            if (productResult.Product is not { } product)
                return (PurchaseOutcome.Failed, StoreUnreachable);
            var result = await product.RequestPurchaseAsync();
            return result.Status switch
            {
                StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased => (PurchaseOutcome.Purchased, null),
                StorePurchaseStatus.NotPurchased => (PurchaseOutcome.Cancelled, null),
                StorePurchaseStatus.NetworkError => (PurchaseOutcome.Failed, StoreUnreachable),
                _ => (PurchaseOutcome.Failed, PurchaseFailed),
            };
        }
        catch (Exception)
        {
            return (PurchaseOutcome.Failed, PurchaseFailed);
        }
    }

    // A desktop app's StoreContext needs the window its purchase dialog belongs to, so it's
    // made on first use, once the main window exists.
    private StoreContext Context
    {
        get
        {
            if (_context is null)
            {
                _context = StoreContext.GetDefault();
                WinRT.Interop.InitializeWithWindow.Initialize(_context, App.MainWindow.Handle);
                _context.OfflineLicensesChanged += (_, _) =>
                    App.MainWindow.DispatcherQueue.TryEnqueue(() => _ = StoreLicense.RefreshAsync());
            }
            return _context;
        }
    }
}
