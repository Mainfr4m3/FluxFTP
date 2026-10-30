namespace IoFtp.Desktop.Services;

// Shared only by sources writing the same release to the same destination.
// A source that has no SFV never creates a requirement of its own.
internal sealed class RaceSfvBarrier
{
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _required = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _ready = new(StringComparer.Ordinal);

    public void Observe(string destination, long? size)
    {
        lock (_gate)
            _required[destination] = Math.Max(_required.GetValueOrDefault(destination), Math.Max(0, size ?? 0));
    }

    // Called only after successful completion or an equal positive target SIZE.
    public void Confirm(string destination, long size)
    {
        lock (_gate)
        {
            if (size <= 0 || !_required.ContainsKey(destination)) return;
            _required[destination] = Math.Max(_required[destination], size);
            _ready[destination] = Math.Max(_ready.GetValueOrDefault(destination), size);
        }
    }

    public int PendingCount
    {
        get { lock (_gate) return _required.Count(pair => pair.Value <= 0 || _ready.GetValueOrDefault(pair.Key) < pair.Value); }
    }

    public IReadOnlyList<T> Select<T>(IEnumerable<T> files, Func<T, string> destination)
    {
        lock (_gate)
        {
            var pending = _required.Where(pair => pair.Value <= 0 || _ready.GetValueOrDefault(pair.Key) < pair.Value)
                .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
            return (pending.Count == 0 ? files : files.Where(file => pending.Contains(destination(file)))).ToArray();
        }
    }
}
