using PhotoRecull.Core.Library;

namespace PhotoRecull.Core.Settings;

public enum TargetKind
{
    OneDrive,
    SynologyPhotos,
}

public record TargetSettings(TargetKind Kind, string FolderPath);

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
