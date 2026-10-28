using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using PhotoRecull.App.Services;

namespace PhotoRecull.App;

// Replaces the XAML-generated entry point (DISABLE_XAML_GENERATED_MAIN) so the
// app can check for an already running instance before any window is created.
public static partial class Program
{
    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var mainInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey());
        if (!mainInstance.IsCurrent)
        {
            HandOverToRunningInstance(mainInstance);
            return 0;
        }

        mainInstance.Activated += (_, _) => App.BringMainWindowToFront();
        Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    // One instance per data folder: the library and settings must have a single
    // writer, while a throwaway PHOTORECULL_DATA_FOLDER can still run alongside.
    private static string SingleInstanceKey() =>
        "PhotoRecull-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(AppDataPaths.Root.ToUpperInvariant())))[..16];

    // Redirecting is asynchronous, but this STA thread mustn't block in a way that
    // stops COM calls from being pumped, so wait with CoWaitForMultipleObjects.
    private static void HandOverToRunningInstance(AppInstance mainInstance)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        using var redirected = new ManualResetEvent(false);
        Task.Run(() =>
        {
            mainInstance.RedirectActivationToAsync(activation).AsTask().Wait();
            redirected.Set();
        });
        _ = CoWaitForMultipleObjects(0, uint.MaxValue, 1, [redirected.SafeWaitHandle.DangerousGetHandle()], out _);

        // Only the process the user just started may move another window to the
        // foreground, so the hand-over does it here rather than in the running app.
        var runningWindow = Process.GetProcessById((int)mainInstance.ProcessId).MainWindowHandle;
        if (runningWindow != IntPtr.Zero)
            SetForegroundWindow(runningWindow);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoWaitForMultipleObjects(
        uint flags, uint timeoutMilliseconds, uint handleCount, IntPtr[] handles, out uint signaledIndex);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr windowHandle);
}
