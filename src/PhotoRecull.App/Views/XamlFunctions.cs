using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PhotoRecull.App.ViewModels;

namespace PhotoRecull.App.Views;

public static class XamlFunctions
{
    public static bool IsNotEmpty(string? text) => !string.IsNullOrEmpty(text);

    public static Visibility VisibleWhenNotEmpty(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility CollapsedWhenTrue(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static string ConnectionLabel(bool isConnected) => isConnected ? "Connected" : "Not connected";

    public static Brush ValidationResultBrush(bool isValid, bool isChecking) =>
        ThemeBrush(isChecking ? "TextFillColorSecondaryBrush"
            : isValid ? "SystemFillColorSuccessBrush"
            : "SystemFillColorCriticalBrush");

    public static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    public static Brush CardBorderBrush(bool isHighlighted) =>
        ThemeBrush(isHighlighted ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");

    public static Brush PillBackground(LibraryCardStatus status) => ThemeBrush($"Pill{status}Background");

    public static Brush PillForeground(LibraryCardStatus status) => ThemeBrush($"Pill{status}Foreground");

    public static Brush ChipBackground(bool isSelected) =>
        ThemeBrush(isSelected ? "PillPickedBackground" : "CardBackgroundFillColorDefaultBrush");

    public static Brush ChipBorder(bool isSelected) =>
        ThemeBrush(isSelected ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");

    public static Brush ChipForeground(bool isSelected) =>
        ThemeBrush(isSelected ? "PillPickedForeground" : "TextFillColorPrimaryBrush");

    public static Brush DoneOrAccentBrush(bool isDone) =>
        ThemeBrush(isDone ? "SystemFillColorSuccessBrush" : "AccentFillColorDefaultBrush");

    public static Brush DoneOrPrimaryBrush(bool isDone) =>
        ThemeBrush(isDone ? "SystemFillColorSuccessBrush" : "TextFillColorPrimaryBrush");

    public static string TabText(string label, int count) => $"{label} · {count:N0}";

    public static Thickness BurstPadding(bool isBurst) => isBurst ? new Thickness(10, 8, 10, 10) : new Thickness(0);

    public static Brush BurstBackground(bool isBurst) =>
        isBurst ? ThemeBrush("SubtleFillColorSecondaryBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public static Brush KeepButtonBackground(bool isKept) =>
        ThemeBrush(isKept ? "PillDoneBackground" : "ControlFillColorDefaultBrush");

    public static Brush KeepButtonForeground(bool isKept) =>
        ThemeBrush(isKept ? "PillDoneForeground" : "TextFillColorPrimaryBrush");

    public static string PreviewKeepText(bool isKept) => isKept ? "Kept · Undo (K)" : "Keep this photo (K)";

    public static bool CanAct(bool hasCandidates, bool isActing) => hasCandidates && !isActing;

    public static bool Not(bool value) => !value;

    public static Microsoft.UI.Xaml.Controls.InfoBarSeverity ResultSeverity(bool hasFailures) =>
        hasFailures ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;

    public static Brush FailureCountBrush(int failedCount) =>
        ThemeBrush(failedCount > 0 ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
}
