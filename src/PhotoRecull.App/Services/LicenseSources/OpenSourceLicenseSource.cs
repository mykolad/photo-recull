namespace PhotoRecull.App.Services;

// Compiled into every build but the Microsoft Store's (PhotoRecull.App.csproj), which has
// StoreLicenseSource instead.
internal static partial class StoreLicense
{
    private static partial ILicenseSource CreateSource() => new OpenSourceLicenseSource();
}

/// <summary>
/// The open-source app's license: never limited. PHOTORECULL_SIMULATE_TRIAL=1 makes it act as
/// a trial with a made-up price, whose Buy succeeds after half a second, to try the purchase
/// screens without the Store.
/// </summary>
internal sealed class OpenSourceLicenseSource : ILicenseSource
{
    private bool _simulatedPurchase;

    public bool IsLimited => StoreLicense.SimulatesTrial;

    public Task<bool> IsPurchasedAsync() => Task.FromResult(!StoreLicense.SimulatesTrial || _simulatedPurchase);

    public Task<string?> PriceAsync() => Task.FromResult(StoreLicense.SimulatesTrial ? "€9.99 (simulated)" : null);

    public async Task<(PurchaseOutcome Outcome, string? Error)> PurchaseAsync()
    {
        await Task.Delay(500);
        _simulatedPurchase = true;
        return (PurchaseOutcome.Purchased, null);
    }
}
