using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core.Library;

namespace PhotoRecull.App.ViewModels;

/// <summary>Settings' PhotoRecull section: the Store build's trial, and buying it.</summary>
public sealed partial class LicenseSettingsViewModel : ObservableObject
{
    public LicenseSettingsViewModel()
    {
        if (!StoreLicense.IsLimitedBuild)
            return;
        StoreLicense.Changed += OnLicenseChanged;
        _ = LoadAsync();
    }

    // Shown once the Store has answered, so a trial never reads "Purchased" for a moment.
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(StatusText), nameof(IsBuyVisible))]
    public partial bool IsPurchased { get; set; }

    [ObservableProperty]
    public partial string BuyButtonText { get; set; } = "Buy";

    public string Title => IsPurchased ? "Purchased" : "Free trial";

    public string StatusText =>
        IsPurchased ? "Thank you for buying PhotoRecull. Every cleanup can stage and delete."
        : StoreLicense.FreeCleanup() is { } free
            ? $"Your free cleanup is {DateRangeLabel.Format(free.From, free.To)}. Buy PhotoRecull to stage and delete in every cleanup; picking, scanning and review stay free."
            : "Picking, scanning and review are free. Staging and deleting are free in one cleanup: the first one you stage or delete in.";

    public bool IsBuyVisible => !IsPurchased;

    private async Task LoadAsync()
    {
        await StoreLicense.RefreshAsync();
        IsPurchased = StoreLicense.IsPurchased;
        IsVisible = true;
        if (!IsPurchased && await StoreLicense.PriceAsync() is { } price)
            BuyButtonText = $"Buy for {price}";
    }

    [RelayCommand]
    private async Task BuyAsync()
    {
        var (outcome, error) = await StoreLicense.PurchaseAsync();
        IsPurchased = StoreLicense.IsPurchased;
        if (outcome == PurchaseOutcome.Purchased)
            ShellPage.Current?.ShowToast("Thank you for buying PhotoRecull. Every cleanup can now stage and delete.");
        else if (outcome == PurchaseOutcome.Failed)
            ShellPage.Current?.ShowToast(error ?? "The purchase didn't go through.", InfoBarSeverity.Error);
    }

    private void OnLicenseChanged(object? sender, EventArgs e) => IsPurchased = StoreLicense.IsPurchased;

    // The license outlives the page: a closed page stops listening.
    public void Detach() => StoreLicense.Changed -= OnLicenseChanged;
}
