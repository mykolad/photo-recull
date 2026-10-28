using System.Runtime.InteropServices.WindowsRuntime;
using ImageMagick;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace PhotoRecull.App.Services;

// iPhone photos are HEIC, which Windows can only decode with optional codecs
// installed, so photos are decoded here with Magick.NET; videos use the shell's
// thumbnails. Decoding is CPU-bound, so only a few run at once, and results are
// cached for the session so scrolling back doesn't decode again.
internal static class Thumbnails
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov" };
    private static readonly SemaphoreSlim DecodeSlots = new(Math.Max(2, Environment.ProcessorCount / 2));
    // Keyed by the file's version too, so a photo replaced at the same path (and
    // scanned again) never shows the previous file's picture.
    private static readonly Dictionary<(string Path, DateTime LastWriteUtc, long Length, int Size), byte[]> EncodedCache = [];
    private const int MaxCachedThumbnails = 600;

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    public static async Task<ImageSource?> LoadAsync(string path, int longestSidePixels)
    {
        try
        {
            var encoded = IsVideo(path)
                ? await LoadVideoThumbnailAsync(path, longestSidePixels)
                : await LoadPhotoThumbnailAsync(path, longestSidePixels);
            if (encoded is null)
                return null;

            var bitmap = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(encoded.AsBuffer());
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MagickException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> LoadPhotoThumbnailAsync(string path, int longestSidePixels)
    {
        var file = new FileInfo(path);
        var cacheKey = (path, file.LastWriteTimeUtc, file.Length, longestSidePixels);
        lock (EncodedCache)
            if (EncodedCache.TryGetValue(cacheKey, out var cached))
                return cached;

        await DecodeSlots.WaitAsync();
        try
        {
            var encoded = await Task.Run(() =>
            {
                using var image = new MagickImage(path);
                image.AutoOrient();
                image.Thumbnail(new MagickGeometry((uint)longestSidePixels, (uint)longestSidePixels));
                image.Format = MagickFormat.Jpeg;
                image.Quality = 82;
                return image.ToByteArray();
            });
            lock (EncodedCache)
            {
                if (EncodedCache.Count >= MaxCachedThumbnails)
                    EncodedCache.Clear();
                EncodedCache[cacheKey] = encoded;
            }
            return encoded;
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    private static async Task<byte[]?> LoadVideoThumbnailAsync(string path, int longestSidePixels)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)longestSidePixels);
        if (thumbnail is null || thumbnail.Size == 0)
            return null;
        var bytes = new byte[thumbnail.Size];
        using var reader = new DataReader(thumbnail.GetInputStreamAt(0));
        await reader.LoadAsync((uint)thumbnail.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
