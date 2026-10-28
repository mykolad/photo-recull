using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using PhotoRecull.App.Services;
using PhotoRecull.App.Views;
using PhotoRecull.Core;
using PhotoRecull.Core.Settings;

namespace PhotoRecull.App.ViewModels;

public enum ReviewTab
{
    ToRemove,
    ToKeep,
    Skipped,
}

public sealed partial class ReviewTileViewModel : ObservableObject
{
    public const int TileThumbnailPixels = 320;
    public const int PreviewPixels = 1600;

    private readonly Action<ReviewTileViewModel> _toggleKeep;
    private readonly Action<ReviewTileViewModel> _openPreview;
    private bool _thumbnailRequested;

    public ReviewTileViewModel(
        PhotoFile photo, TargetSettings target, ReviewTab tab, bool isKept,
        Action<ReviewTileViewModel> toggleKeep, Action<ReviewTileViewModel> openPreview)
    {
        Photo = photo;
        Target = target;
        Tab = tab;
        IsKept = isKept;
        _toggleKeep = toggleKeep;
        _openPreview = openPreview;
    }

    public PhotoFile Photo { get; }
    public TargetSettings Target { get; }
    public ReviewTab Tab { get; }
    public string FileName => Photo.FileName;
    public string FullPath => Photo.Path;
    public string TargetGlyph => ShellPage.TargetGlyph(Target.Kind);
    public string TargetName => SyncTargets.DisplayName(Target.Kind);
    public bool IsVideo => Thumbnails.IsVideo(Photo.Path);
    public bool CanBeKept => Tab == ReviewTab.ToRemove;
    public string TimeSourceLabel => Photo.TimestampSource switch
    {
        "filename" => "File name",
        "exif" => "EXIF",
        _ => "No capture time",
    };

    // Burst position text ("Frame 3 of 5, within 3 s"), set when grouped.
    public string BurstPositionText { get; set; } = "No";

    public long SizeBytes => _sizeBytes ??= File.Exists(Photo.Path) ? new FileInfo(Photo.Path).Length : 0;
    private long? _sizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeepButtonText), nameof(KeptVeilOpacity))]
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
    public double KeptVeilOpacity => IsKept ? 0.65 : 0;

    public string ShortTimeText(bool inUtc) => Photo.Timestamp is { } time
        ? (inUtc ? time.UtcDateTime : time.ToLocalTime().DateTime).ToString("HH:mm:ss", CultureInfo.CurrentCulture) + (inUtc ? " UTC" : "")
        : "–";

    public string LocalTimeText => Photo.Timestamp is { } time
        ? time.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm:ss", CultureInfo.CurrentCulture)
        : "Unknown";

    public string UtcTimeText => Photo.Timestamp is { } time
        ? time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " UTC"
        : "Unknown";

    public string TypeAndSizeText => $"{(IsVideo ? "Video" : Path.GetExtension(Photo.Path).TrimStart('.').ToUpperInvariant())} · {FileSizeText.Format(SizeBytes)}";

    private async void LoadThumbnailOnce()
    {
        if (_thumbnailRequested)
            return;
        _thumbnailRequested = true;
        Thumbnail = await Thumbnails.LoadAsync(Photo.Path, TileThumbnailPixels);
    }

    [RelayCommand]
    private void ToggleKeep() => _toggleKeep(this);

    [RelayCommand]
    private void OpenPreview() => _openPreview(this);

    [RelayCommand]
    private void OpenOriginal() => BrowserLauncher.Open(Photo.Path);
}

public sealed class ReviewSegmentViewModel(bool isBurst, string burstLabel, List<ReviewTileViewModel> tiles)
{
    public bool IsBurst { get; } = isBurst;
    public string BurstLabel { get; } = burstLabel;
    public List<ReviewTileViewModel> Tiles { get; } = tiles;
}

public sealed class ReviewDayViewModel(string header, List<ReviewSegmentViewModel> segments)
{
    public string Header { get; } = header;
    public List<ReviewSegmentViewModel> Segments { get; } = segments;
}
