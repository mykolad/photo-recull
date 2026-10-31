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

    // PHOTORECULL_DEBUG_OPEN_CLEANUP=<step>:<cleanup id> opens that cleanup's flow
    // and drives it to the step (waiting | range | scan | review | review-expanded |
    // preview | stage) using its saved pick, without signing in to Google, so each
    // step can be captured.
    public static (string Step, Guid CleanupId)? RequestedCleanupStep()
    {
        var request = Environment.GetEnvironmentVariable("PHOTORECULL_DEBUG_OPEN_CLEANUP")?.Split(':');
        return request is [var step, var id] && Guid.TryParse(id, out var cleanupId) ? (step, cleanupId) : null;
    }

    public static async Task DriveFlowToStepAsync(ViewModels.CleanupFlowViewModel flow, string step)
    {
        switch (step)
        {
            case "waiting":
                flow.IsSavedPickVisible = false;
                flow.IsPicking = true;
                break;
            case "range":
                await flow.ContinueFromSavedPickCommand.ExecuteAsync(null);
                break;
            case "scan":
                await flow.ContinueFromSavedPickCommand.ExecuteAsync(null);
                await flow.ScanTargetsCommand.ExecuteAsync(null);
                break;
            case "review" or "review-expanded" or "preview" or "stage":
                await flow.ContinueFromSavedPickCommand.ExecuteAsync(null);
                await flow.ScanTargetsCommand.ExecuteAsync(null);
                flow.ReviewResultsCommand.Execute(null);
                var stacks = flow.Review?.Days.SelectMany(d => d.Stacks).ToList() ?? [];
                if (step == "review-expanded" && stacks.FirstOrDefault(s => s.IsBurst) is { } burst)
                    burst.ToggleExpandedCommand.Execute(null);
                if (step == "preview" && stacks.FirstOrDefault() is { } stack)
                    stack.Cover.OpenPreviewCommand.Execute(null);
                if (step == "stage")
                    await flow.StageAsync(flow.CandidatesPerTarget().ToDictionary(t => t.Target, t => flow.DefaultStageFolderFor(t.Target)));
                break;
        }
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
