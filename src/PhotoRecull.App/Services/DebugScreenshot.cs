#if DEBUG
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PhotoRecull.App.Services;

// Lets a developer (or an agent) check a screen without a visible desktop, for
// example while the PC is locked: the app renders its own window content to a
// PNG and exits. Debug builds only.
internal static class DebugScreenshot
{
    public static void CaptureIfRequested(Window window, FrameworkElement root)
    {
        var outputPath = Environment.GetEnvironmentVariable("PHOTORECULL_CAPTURE_PNG");
        if (string.IsNullOrEmpty(outputPath))
            return;

        root.Loaded += async (_, _) =>
        {
            var delaySeconds = int.TryParse(Environment.GetEnvironmentVariable("PHOTORECULL_CAPTURE_DELAY_SECONDS"), out var seconds)
                ? seconds
                : 3;
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            await SavePngAsync(root, outputPath);
            window.Close();
        };
    }

    private static async Task SavePngAsync(UIElement element, string outputPath)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();

        stream.Seek(0);
        await using var file = File.Create(outputPath);
        await stream.AsStreamForRead().CopyToAsync(file);
    }
}
#endif
