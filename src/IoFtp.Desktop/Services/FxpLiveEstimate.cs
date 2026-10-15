using System.Diagnostics;

namespace IoFtp.Desktop.Services;

// Display-only estimates. Never use these bytes for completion, resume or accounting.
internal sealed class FxpLiveEstimate(long total, long offset, long assumedSpeed, Func<TimeSpan>? clock = null)
{
    private readonly object _gate = new();
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private TimeSpan? _started;
    private TimeSpan _sampleAt;
    private long _sampleBytes = Math.Max(0, offset), _displayBytes = Math.Max(0, offset);
    private double _speed = Math.Max(1, assumedSpeed);
    private TimeSpan Now => clock?.Invoke() ?? _watch.Elapsed;
    public bool Started { get { lock (_gate) return _started.HasValue; } }
    public void Start() { lock (_gate) { if (_started is null) _started = _sampleAt = Now; } }
    public bool Observe(long bytes)
    {
        lock (_gate)
        {
            // Full-size/preallocated targets cannot prove current progress before 226.
            if (_started is null || total <= 0 || bytes < _sampleBytes || bytes >= total || bytes <= offset) return false;
            var seconds = (Now - _started.Value).TotalSeconds;
            if (seconds <= 0) return false;
            var current = (bytes - offset) / seconds;
            _speed = _speed * (current < _speed ? .9 : .7) + current * (current < _speed ? .1 : .3);
            _sampleBytes = bytes; _sampleAt = Now;
            return true;
        }
    }
    public (long Bytes, long Speed) Read()
    {
        lock (_gate)
        {
            if (_started is null) return (Math.Max(0, offset), 0);
            var interpolated = _sampleBytes + _speed * Math.Max(0, (Now - _sampleAt).TotalSeconds);
            var limit = total > 0 ? Math.Max(offset, total - 1) : Math.Max(0, offset);
            _displayBytes = Math.Min(limit, Math.Max(_displayBytes, interpolated >= long.MaxValue ? long.MaxValue : (long)interpolated));
            return (_displayBytes, _speed >= long.MaxValue ? long.MaxValue : (long)_speed);
        }
    }
}

internal sealed class FxpPairSpeedHistory
{
    private readonly Dictionary<(Guid Source, Guid Destination), long> _speeds = [];
    public long Get(Guid source, Guid destination) => _speeds.GetValueOrDefault((source, destination), 1_048_576);
    public void Record(Guid source, Guid destination, long bytes, TimeSpan elapsed)
    {
        if (PassiveFxpAverage.Calculate(bytes, elapsed) is not { } speed) return;
        _speeds[(source, destination)] = (long)Math.Min(long.MaxValue, .8 * Get(source, destination) + .2 * speed);
    }
}
