using PhotoRecull.Core.Library;

namespace PhotoRecull.Core.Settings;

// A folder a cleanup repeats the Google Photos culls in, and what it's called.
public record TargetSettings(string Name, string FolderPath)
{
    // Photos from other cameras are skipped unless the user turns this off: an
    // iPhone's saves its time zone, so its capture times match Google's exactly.
    public bool OnlyIphonePhotos { get; init; } = true;
}

public record AppSettings
{
    // null means the system default browser; otherwise a value BrowserLauncher accepts.
    public string? SignInBrowser { get; init; }

    public List<TargetSettings> Targets { get; init; } = [];

    public bool ShowCaptureTimesInUtc { get; init; }

    public ReviewFilter ReviewFilter { get; init; }

    // Only used by builds without a built-in Google OAuth client (forks, local
    // development); Store builds have one baked in.
    public string? DeveloperCredentialsPath { get; init; }

    public DateTimeOffset? SetupCompletedAt { get; init; }
}
