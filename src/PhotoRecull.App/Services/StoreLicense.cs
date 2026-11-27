using PhotoRecull.Core.Library;
using PhotoRecull.Core.Licensing;

namespace PhotoRecull.App.Services;

public enum PurchaseOutcome
{
    Purchased,
    Cancelled,
    Failed,
}

/// <summary>
/// Where a build's license comes from. The project compiles in one of two: the Store build's
/// <c>StoreLicenseSource</c>, which asks the Microsoft Store, or every other build's
/// <c>OpenSourceLicenseSource</c>, which is never limited (see PhotoRecull.App.csproj).
/// </summary>
internal interface ILicenseSource
{
    /// <summary>Whether this build can be a trial at all, so Settings shows its license.</summary>
    bool IsLimited { get; }

    Task<bool> IsPurchasedAsync();

    /// <summary>The app's price, formatted for this PC's market, or null.</summary>
    Task<string?> PriceAsync();

    Task<(PurchaseOutcome Outcome, string? Error)> PurchaseAsync();
}

/// <summary>
/// Whether this copy is bought, and what a trial may do. Only the Microsoft Store build is
/// paid: it's a paid app with an unlimited free trial, and the trial stages and deletes in
/// one cleanup (<see cref="TrialAllowance"/>). Every other build (dotnet run, a fork, the
/// command-line app) is the open-source app and never limited.
/// </summary>
internal static partial class StoreLicense
{
    private static readonly ILicenseSource Source = CreateSource();

    // Each build's license source file gives the one it was built with.
    private static partial ILicenseSource CreateSource();

    /// <summary>
    /// PHOTORECULL_SIMULATE_TRIAL=1 makes a build act as unbought, to try the trial: see the
    /// license sources for what each build does with it.
    /// </summary>
    public static bool SimulatesTrial { get; } =
        Environment.GetEnvironmentVariable("PHOTORECULL_SIMULATE_TRIAL") == "1";

    public static bool IsLimitedBuild => Source.IsLimited;

    // Until the license is read, a copy counts as bought: nobody who paid is ever held back by
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
        IsPurchased = await Source.IsPurchasedAsync();
        if (IsPurchased != wasPurchased)
            Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Task<string?> PriceAsync() => Source.PriceAsync();

    /// <summary>Buys the app: the Store build opens the Store's own purchase window.</summary>
    public static async Task<(PurchaseOutcome Outcome, string? Error)> PurchaseAsync()
    {
        var result = await Source.PurchaseAsync();
        if (result.Outcome == PurchaseOutcome.Purchased)
            await RefreshAsync();
        return result;
    }
}
