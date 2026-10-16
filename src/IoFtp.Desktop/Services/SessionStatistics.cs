using System.Diagnostics;

namespace IoFtp.Desktop.Services;

internal sealed class SessionStatistics(Func<TimeSpan>? elapsed = null)
{
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private long _uploaded, _downloaded, _fxp, _local, _completed, _created, _closed, _hits, _peakSpeed;
    public long PeakSpeedBytesPerSecond => Interlocked.Read(ref _peakSpeed);
    public void RecordSpeed(long bytesPerSecond)
    {
        long previous;
        do
        {
            previous = Interlocked.Read(ref _peakSpeed);
            if (bytesPerSecond <= previous) return;
        } while (Interlocked.CompareExchange(ref _peakSpeed, bytesPerSecond, previous) != previous);
    }
    public TimeSpan Uptime => elapsed?.Invoke() ?? _uptime.Elapsed;
    private readonly object _rateGate = new();
    private readonly Dictionary<long, long> _trafficMinutes = new();
    public IReadOnlyList<(long Minute, long Bytes)> TrafficMinutes
    {
        get
        {
            lock (_rateGate)
            {
                var minute = (long)Uptime.TotalMinutes;
                var first = Math.Max(0, minute - 59);
                return Enumerable.Range(0, (int)(minute - first + 1))
                    .Select(offset => (Minute: first + offset, Bytes: _trafficMinutes.GetValueOrDefault(first + offset))).ToArray();
            }
        }
    }
    private readonly Dictionary<long, long> _fxpSeconds = new();
    public long FxpRecentRate
    {
        get
        {
            lock (_rateGate)
            {
                var second = (long)Uptime.TotalSeconds;
                foreach (var old in _fxpSeconds.Keys.Where(value => value <= second - 60).ToArray()) _fxpSeconds.Remove(old);
                return _fxpSeconds.Values.Sum() / 60;
            }
        }
    }
    public long FxpSessionRate => (long)(Fxp / Math.Max(1, Uptime.TotalSeconds));
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
        lock (_rateGate)
        {
            var minute = (long)Uptime.TotalMinutes;
            foreach (var old in _trafficMinutes.Keys.Where(value => value < minute - 59).ToArray()) _trafficMinutes.Remove(old);
            _trafficMinutes[minute] = _trafficMinutes.GetValueOrDefault(minute) + bytes;
        }
        switch (kind)
        {
            case "Upload": Interlocked.Add(ref _uploaded, bytes); break;
            case "Download": Interlocked.Add(ref _downloaded, bytes); break;
            case "FXP":
                Interlocked.Add(ref _fxp, bytes);
                lock (_rateGate)
                {
                    var second = (long)Uptime.TotalSeconds;
                    foreach (var old in _fxpSeconds.Keys.Where(value => value <= second - 60).ToArray()) _fxpSeconds.Remove(old);
                    _fxpSeconds[second] = _fxpSeconds.GetValueOrDefault(second) + bytes;
                }
                break;
            case "Local": Interlocked.Add(ref _local, bytes); break;
            default: throw new ArgumentException("Unknown transfer kind", nameof(kind));
        }
        Interlocked.Increment(ref _completed);
    }
}
