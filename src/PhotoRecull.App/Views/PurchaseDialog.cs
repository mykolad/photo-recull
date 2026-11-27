using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoRecull.App.Services;
using PhotoRecull.Core.Library;

namespace PhotoRecull.App.Views;

/// <summary>
/// Asked before a stage or delete the free trial doesn't cover: buy now (the Store's own
/// purchase window), or leave the cleanup reviewed and untouched.
/// </summary>
internal static class PurchaseDialog
{
    /// <summary>Whether the stage or delete may go ahead: always for a bought copy.</summary>
    public static async Task<bool> AllowActAsync(XamlRoot xamlRoot, Guid? cleanupId)
    {
        if (await StoreLicense.MayActAsync(cleanupId))
            return true;
        return await AskToBuyAsync(xamlRoot, StoreLicense.FreeCleanup());
    }

    public static async Task<bool> AskToBuyAsync(XamlRoot xamlRoot, Cleanup? freeCleanup)
    {
        string? price = await StoreLicense.PriceAsync();
        var dialog = ConfirmDialogContent.Create(xamlRoot, "Buy PhotoRecull to keep cleaning up");
        dialog.PrimaryButtonText = price is null ? "Buy" : $"Buy for {price}";
        dialog.CloseButtonText = "Not now";
        dialog.DefaultButton = ContentDialogButton.Primary;

        var content = new StackPanel { Spacing = 12, Width = 520 };
        content.Children.Add(new TextBlock
        {
            Text = freeCleanup is null
                ? "The free trial stages and deletes in one cleanup."
                : $"The free trial stages and deletes in one cleanup, and you've used it on {DateRangeLabel.Format(freeCleanup.From, freeCleanup.To)}.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Buy PhotoRecull once to stage and delete in this cleanup and every one after. It's yours on every PC you sign in to with the same Microsoft account. Picking, scanning and review stay free, and nothing more is moved or deleted unless you buy.",
            TextWrapping = TextWrapping.Wrap,
        });
        dialog.Content = content;

        // The Store's purchase window opens once this dialog has closed: two modal windows
        // over the app at once is one too many.
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return false;
        var (outcome, error) = await StoreLicense.PurchaseAsync();
        switch (outcome)
        {
            case PurchaseOutcome.Purchased:
                ShellPage.Current?.ShowToast("Thank you for buying PhotoRecull. Every cleanup can now stage and delete.");
                return true;
            case PurchaseOutcome.Failed:
                ShellPage.Current?.ShowToast(error ?? "The purchase didn't go through.", InfoBarSeverity.Error);
                return false;
            default:
                return false;
        }
    }
}
