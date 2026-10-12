using System.Diagnostics;

namespace IoFtp.Desktop.Services;

internal sealed class SessionStatistics
{
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private long _uploaded, _downloaded, _fxp, _local, _completed, _created, _closed, _hits;
    public TimeSpan Uptime => _uptime.Elapsed;
    public long Uploaded => Interlocked.Read(ref _uploaded);
    public long Downloaded => Interlocked.Read(ref _downloaded);
    public long Fxp => Interlocked.Read(ref _fxp);
    public long Local => Interlocked.Read(ref _local);
    public long Completed => Interlocked.Read(ref _completed);
    public long Created => Interlocked.Read(ref _created);
    public long Closed => Interlocked.Read(ref _closed);
    public long Hits => Interlocked.Read(ref _hits);
    public void WorkerCreated() => Interlocked.Increment(ref _created);
    public void WorkerClosed() => Interlocked.Increment(ref _closed);
    public void PoolHit() => Interlocked.Increment(ref _hits);
    public void Complete(string kind, long newBytes)
    {
        var bytes = Math.Max(0, newBytes);
        switch (kind)
        {
            case "Upload": Interlocked.Add(ref _uploaded, bytes); break;
            case "Download": Interlocked.Add(ref _downloaded, bytes); break;
            case "FXP": Interlocked.Add(ref _fxp, bytes); break;
            case "Local": Interlocked.Add(ref _local, bytes); break;
            default: throw new ArgumentException("Unknown transfer kind", nameof(kind));
        }
        Interlocked.Increment(ref _completed);
    }
}
