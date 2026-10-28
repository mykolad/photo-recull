using Microsoft.Win32;

namespace PhotoRecull.App.Services;

internal static class DefaultBrowser
{
    public static string? FriendlyNameOrNull()
    {
        using var userChoice = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        var progId = userChoice?.GetValue("ProgId") as string ?? "";

        if (progId.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (progId.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (progId.StartsWith("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        return null;
    }
}
