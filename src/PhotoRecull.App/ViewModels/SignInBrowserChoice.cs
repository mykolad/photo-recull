using PhotoRecull.App.Services;

namespace PhotoRecull.App.ViewModels;

public sealed record SignInBrowserChoice(string Label, string? BrowserLauncherValue, bool IsCustomPath = false)
{
    public static IReadOnlyList<SignInBrowserChoice> All { get; } =
    [
        new(DefaultBrowser.FriendlyNameOrNull() is { } name ? $"System default ({name})" : "System default", null),
        new("Chrome", "chrome"),
        new("Edge", "edge"),
        new("Firefox", "firefox"),
        new("Custom path…", null, IsCustomPath: true),
    ];

    public static int IndexFor(string? signInBrowser) => signInBrowser switch
    {
        null or "" => 0,
        "chrome" => 1,
        "edge" => 2,
        "firefox" => 3,
        _ => 4,
    };

    // What's saved as AppSettings.SignInBrowser: a custom path left empty means the
    // system default.
    public static string? SettingFor(int index, string customPath)
    {
        var choice = All[Math.Clamp(index, 0, All.Count - 1)];
        return choice.IsCustomPath
            ? (string.IsNullOrWhiteSpace(customPath) ? null : customPath.Trim())
            : choice.BrowserLauncherValue;
    }

    public string DisplayNameInSentence => BrowserLauncherValue is null && !IsCustomPath
        ? DefaultBrowser.FriendlyNameOrNull() ?? "your browser"
        : IsCustomPath ? "your browser" : Label;

    public static string DisplayNameFor(string? signInBrowser)
    {
        var choice = All[IndexFor(signInBrowser)];
        return choice.IsCustomPath ? Path.GetFileNameWithoutExtension(signInBrowser!) : choice.DisplayNameInSentence;
    }
}
