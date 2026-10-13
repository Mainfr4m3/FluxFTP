namespace IoFtp.Desktop.Services;

// Session-only hints from configured announcement bots or the local VISIONARY bridge.
internal sealed class RaceCompletionSignals(Func<DateTimeOffset>? clock = null)
{
    public static RaceCompletionSignals Shared { get; } = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _signals = new(StringComparer.OrdinalIgnoreCase);
    private static string Key(string site, string section, string release) => string.Join('\t', site, section, release);
    public void Record(string site, string section, string release)
    {
        if (new[] { site, section, release }.Any(value => string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))) return;
        lock (_gate)
        {
            var now = clock?.Invoke() ?? DateTimeOffset.UtcNow;
            foreach (var key in _signals.Where(pair => now - pair.Value >= TimeSpan.FromMinutes(10)).Select(pair => pair.Key).ToArray()) _signals.Remove(key);
            if (_signals.Count >= 1024) _signals.Remove(_signals.MinBy(pair => pair.Value).Key);
            _signals[Key(site, section, release)] = now;
        }
    }
    public bool Has(string site, string section, string release)
    {
        lock (_gate) return _signals.TryGetValue(Key(site, section, release), out var at) &&
            (clock?.Invoke() ?? DateTimeOffset.UtcNow) - at < TimeSpan.FromMinutes(10);
    }
}
