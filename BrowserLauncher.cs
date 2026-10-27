using System.Diagnostics;

namespace PhotoRecull;

/// <summary>
/// Opens a URL or local file in a chosen browser, falling back to the system
/// default. Lets the user route Google interactions to a browser that is
/// signed into their Google account, while keeping their default browser
/// signed out.
/// </summary>
public static class BrowserLauncher
{
    /// <summary>
    /// Opens <paramref name="target"/> (a URL or file path). <paramref name="browser"/>
    /// may be a friendly name (chrome/edge/firefox), an executable name or full
    /// path, or null/"default" to use the system default handler.
    /// Returns true if the launch was started successfully.
    /// </summary>
    public static bool Open(string target, string? browser = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(browser) ||
                browser.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                // System default handler.
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo(ResolveBrowser(browser))
                {
                    Arguments = QuoteIfNeeded(target),
                    UseShellExecute = true,
                });
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Friendly names map to the executable/App-Paths alias ShellExecute resolves;
    // anything else is passed through as an executable name or full path.
    private static string ResolveBrowser(string browser) => browser.Trim().ToLowerInvariant() switch
    {
        "chrome"           => "chrome",
        "edge" or "msedge" => "msedge",
        "firefox"          => "firefox",
        _                  => browser,
    };

    private static string QuoteIfNeeded(string s) =>
        s.Contains(' ') && !s.StartsWith('"') ? $"\"{s}\"" : s;
}
