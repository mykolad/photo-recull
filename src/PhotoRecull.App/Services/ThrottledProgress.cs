using System.Diagnostics;

namespace PhotoRecull.App.Services;

// Scans report every file, which for tens of thousands of files would flood the
// UI thread with updates nobody can read; pass on at most one per interval.
internal sealed class ThrottledProgress<T>(IProgress<T> inner, TimeSpan interval) : IProgress<T>
{
    private readonly Stopwatch _sinceLastReport = Stopwatch.StartNew();
    private bool _hasReported;

    public void Report(T value)
    {
        if (_hasReported && _sinceLastReport.Elapsed < interval)
            return;
        _hasReported = true;
        _sinceLastReport.Restart();
        inner.Report(value);
    }
}
