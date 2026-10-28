namespace PhotoRecull.Cli;

// Progress<T> posts each report to the thread pool when there's no
// SynchronizationContext (as in a console app), so reports can arrive out of
// order and interleave the console output. This reports synchronously instead.
internal sealed class ConsoleProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
