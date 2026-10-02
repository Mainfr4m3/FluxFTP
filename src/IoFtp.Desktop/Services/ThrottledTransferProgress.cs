using System.Diagnostics;

namespace IoFtp.Desktop.Services;

// Throttle before posting to the dispatcher, not after filling its message queue.
internal sealed class ThrottledTransferProgress(IProgress<long> target) : IProgress<long>
{
    private long _last;
    public void Report(long value)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref _last);
        if (previous != 0 && Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds < 100) return;
        if (Interlocked.CompareExchange(ref _last, now, previous) == previous) target.Report(value);
    }
}
