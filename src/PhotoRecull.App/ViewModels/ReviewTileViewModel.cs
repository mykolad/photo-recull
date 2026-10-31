using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public enum ReviewTab
{
    Timeline,
    Skipped,
}

public enum ReviewTileKind
{
    Match,
    Candidate,
    Skipped,
}

public sealed partial class ReviewTileViewModel : ObservableObject
{
    public const int TileThumbnailPixels = 320;
    public const int PreviewPixels = 1600;

    private readonly Action<ReviewTileViewModel> _toggleKeep;
    private readonly Action<ReviewTileViewModel> _openPreview;
    private readonly Func<bool> _isActionRunning;
    private bool _thumbnailRequested;

    public ReviewTileViewModel(
        PhotoFile photo, TargetSettings target, ReviewTileKind kind, bool isKept,
        Action<ReviewTileViewModel> toggleKeep, Action<ReviewTileViewModel> openPreview, Func<bool> isActionRunning)
    {
        Photo = photo;
        Target = target;
        Kind = kind;
        IsKept = isKept;
        _toggleKeep = toggleKeep;
        _openPreview = openPreview;
        _isActionRunning = isActionRunning;
    }

    public PhotoFile Photo { get; }
    public TargetSettings Target { get; }
    public ReviewTileKind Kind { get; }
    public string FileName => Photo.FileName;
    public string FullPath => Photo.Path;
    public string TargetGlyph => ShellPage.TargetGlyph(Target.Kind);
    public string TargetName => SyncTargets.DisplayName(Target.Kind);
    public bool IsVideo => Thumbnails.IsVideo(Photo.Path);
    public bool CanBeKept => Kind == ReviewTileKind.Candidate;
    public string TimeSourceLabel => Photo.TimestampSource switch
    {
        "filename" => "File name",
        "exif" => "EXIF",
        _ => "No capture time",
    };

    // Burst position text ("Frame 3 of 5, within 3 s"), set when grouped.
    public string BurstPositionText { get; set; } = "No";

    public long SizeBytes => Photo.SizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeepButtonText), nameof(Mark), nameof(IsMarkedToRemove), nameof(MarkText), nameof(ThumbnailOpacity))]
    public partial bool IsKept { get; set; }

    // Read by the tile's Image binding when the tile is first laid out, which is
    // exactly when decoding should start; tiles that are never shown never decode.
    public ImageSource? Thumbnail
    {
        get
        {
            LoadThumbnailOnce();
            return _thumbnail;
        }
        private set => SetProperty(ref _thumbnail, value);
    }
    private ImageSource? _thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeLine))]
    public partial bool ShowTimesInUtc { get; set; }

    public string TimeLine => $"{ShortTimeText(ShowTimesInUtc)} · {TimeSourceLabel}";

    public string KeepButtonText => IsKept ? "Undo" : "Keep";
    public ReviewMark Mark => Kind != ReviewTileKind.Candidate ? ReviewMark.Stays
        : IsKept ? ReviewMark.KeptByYou
        : ReviewMark.ToRemove;

    public bool IsMarkedToRemove => Mark == ReviewMark.ToRemove;

    public string MarkText => Kind == ReviewTileKind.Skipped ? "Skipped" : Mark switch
    {
        ReviewMark.ToRemove => "To remove",
        ReviewMark.KeptByYou => "Kept by you",
        _ => "Stays",
    };

    // Dimmed rather than hidden, so a photo to remove is still seen next to the
    // ones around it that stay.
    public double ThumbnailOpacity => IsMarkedToRemove ? 0.4 : 1;

    public string ShortTimeText(bool inUtc) => Photo.Timestamp is { } time
        ? (inUtc ? time.UtcDateTime : time.ToLocalTime().DateTime).ToString("HH:mm:ss", CultureInfo.CurrentCulture) + (inUtc ? " UTC" : "")
        : "–";

    public string LocalTimeText => Photo.Timestamp is { } time
        ? time.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm:ss", CultureInfo.CurrentCulture)
        : "Unknown";

    public string UtcTimeText => Photo.Timestamp is { } time
        ? time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " UTC"
        : "Unknown";

    public string TypeAndSizeText => $"{(IsVideo ? "Video" : Extension(Photo.Path))}{(Photo.LivePhotoVideoPath is { } video ? $" + {Extension(video)} (Live Photo)" : "")} · {FileSizeText.Format(SizeBytes)}";

    private static string Extension(string path) => Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

    private async void LoadThumbnailOnce()
    {
        if (_thumbnailRequested)
            return;
        _thumbnailRequested = true;
        Thumbnail = await Thumbnails.LoadAsync(Photo.Path, TileThumbnailPixels);
    }

    // Once a stage or delete has taken its list of photos, Keep can no longer
    // change what happens to them, so it's unavailable until the run finishes.
    [RelayCommand(CanExecute = nameof(CanToggleKeep))]
    private void ToggleKeep() => _toggleKeep(this);

    private bool CanToggleKeep() => !_isActionRunning();

    [RelayCommand]
    private void OpenPreview() => _openPreview(this);

    [RelayCommand]
    private void OpenOriginal() => BrowserLauncher.Open(Photo.Path);
}

