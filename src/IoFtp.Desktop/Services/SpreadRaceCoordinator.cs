namespace IoFtp.Desktop.Services;

// Same growing-release step used by VISIONARY, supervised until verified at every target.
internal sealed class SpreadRaceCoordinator
{
    private readonly object _gate = new();
    private readonly string[] _sites;
    private readonly HashSet<string> _downloadOnly;
    private readonly Dictionary<string, VisionaryRaceState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _done = new(StringComparer.OrdinalIgnoreCase);
    private string _status = "RUNNING";
    private string? _source;
    public string Error { get; private set; } = "";
    public string Status { get { lock (_gate) return _status; } }
    public string[] Incomplete { get { lock (_gate) return _sites.Where(site => !_downloadOnly.Contains(site) && !site.Equals(_source, StringComparison.OrdinalIgnoreCase) && !_done.Contains(site)).ToArray(); } }

    public SpreadRaceCoordinator(IEnumerable<string> sites, IEnumerable<string> downloadOnly)
    {
        _sites = sites.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _downloadOnly = new(downloadOnly, StringComparer.OrdinalIgnoreCase);
        if (_sites.Length < 2 || _sites.All(_downloadOnly.Contains)) throw new ArgumentException("At least two distinct sites and an upload target are required.");
    }

    public async Task RunAsync(Func<string, string, VisionaryRaceState, CancellationToken, Task<bool>> step,
        VisionaryRaceOptions options, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(options.TimeoutMinutes));
        try
        {
            while (true)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                foreach (var target in Incomplete)
                {
                    var candidates = _source is null ? _sites.Where(site => !site.Equals(target, StringComparison.OrdinalIgnoreCase)).OrderByDescending(_downloadOnly.Contains).ToArray() : [_source];
                    foreach (var source in candidates)
                    {
                        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
                        var state = _states.TryGetValue(target, out var existing) ? existing : new VisionaryRaceState(options);
                        var complete = await step(source, target, state, lifetime.Token);
                        if (!state.SourceAvailable) continue;
                        lock (_gate) { _source ??= source; _states[target] = state; if (complete) _done.Add(target); }
                        break;
                    }
                }
                if (_source is not null && Incomplete.Length == 0) { lock (_gate) _status = "DONE"; return; }
                await Task.Delay(TimeSpan.FromSeconds(options.RefreshSeconds), lifetime.Token);
            }
        }
        catch (OperationCanceledException) { lock (_gate) { _status = cancellationToken.IsCancellationRequested ? "ABORTED" : "FAILED"; Error = "Race cancelled or timed out before destination verification."; } }
        catch (Exception ex) { lock (_gate) { _status = "FAILED"; Error = ex.Message; } }
        finally
        {
            lifetime.Cancel();
            foreach (var pair in _states)
            {
                try { await step(_source!, pair.Key, pair.Value, lifetime.Token); } catch (Exception) { }
            }
        }
    }
}
