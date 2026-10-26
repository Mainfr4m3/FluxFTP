using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
namespace IoFtp.Desktop.Services;

internal enum SiteHealthState { Unknown, Online, Offline, LoginFailed }
internal enum SiteProbeResult { Online, Deferred, Busy, Unreachable, LoginFailed }
internal sealed record SiteHealthPolicy(bool Enabled = false, int CheckSeconds = 300, int RetrySeconds = 60, int Failures = 2, int TimeoutSeconds = 10)
{
    public void Validate()
    {
        if (CheckSeconds is < 30 or > 3600 || RetrySeconds is < 15 or > 3600 || Failures is < 1 or > 10 || TimeoutSeconds is < 1 or > 60)
            throw new ArgumentException("Site monitoring: check 30–3600 seconds, retry 15–3600 seconds, failures 1–10, timeout 1–60 seconds.");
    }
}
internal sealed record SiteHealthSnapshot(Guid Id, string Name, SiteHealthState State, int Failures, DateTimeOffset? CheckedAt, DateTimeOffset NextCheck)
{
    public bool Available => State is not (SiteHealthState.Offline or SiteHealthState.LoginFailed);
}

internal sealed class SiteHealthMonitor(
    Func<ConnectionProfile, CancellationToken, Task<SiteProbeResult>> probe,
    Action<Guid, bool> availability, Action<SiteHealthSnapshot> changed,
    Func<DateTimeOffset>? clock = null) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _sites = [];
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private SiteHealthPolicy _policy = new();
    private int _generation;
    private bool _disposed;
    private DateTimeOffset Now => clock?.Invoke() ?? DateTimeOffset.UtcNow;
    private sealed class Entry(ConnectionProfile profile, DateTimeOffset now)
    {
        public ConnectionProfile Profile = profile;
        public SiteHealthState State;
        public int Failures;
        public int Revision;
        public bool Probing;
        public DateTimeOffset? CheckedAt;
        public DateTimeOffset? LastFailure;
        public DateTimeOffset NextCheck = now;
        public SiteHealthSnapshot Snapshot() => new(Profile.Id, Profile.Name, State, Failures, CheckedAt, NextCheck);
    }
    public void Configure(SiteHealthPolicy policy)
    {
        policy.Validate(); List<SiteHealthSnapshot> reset;
        lock (_gate)
        {
            if (_disposed || policy == _policy) return;
            var enabledChanged = _policy.Enabled != policy.Enabled;
            _policy = policy; _generation++;
            reset = enabledChanged ? _sites.Values.Select(entry => entry.Snapshot()).ToList() : [];
            if (enabledChanged)
                foreach (var entry in _sites.Values) { entry.State = SiteHealthState.Unknown; entry.Failures = 0; entry.LastFailure = null; entry.NextCheck = Now; entry.Revision++; }
        }
        foreach (var entry in reset) availability(entry.Id, true);
    }
    public SiteHealthSnapshot? Snapshot(Guid id) { lock (_gate) return _sites.TryGetValue(id, out var entry) ? entry.Snapshot() : null; }
    public bool IsAvailable(Guid id) { lock (_gate) return !_policy.Enabled || !_sites.TryGetValue(id, out var entry) || entry.Snapshot().Available; }
    public bool IsAvailable(string name) { lock (_gate) return !_policy.Enabled || !_sites.Values.Any(entry => entry.Profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !entry.Snapshot().Available); }
    public async Task WaitForProbeAsync(Guid id, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate) if (!_policy.Enabled || !_sites.TryGetValue(id, out var entry) || !entry.Probing) return;
            await Task.Delay(50, token).ConfigureAwait(false);
        }
    }
    public void ObserveSuccess(ConnectionProfile profile) => Observe(profile, SiteProbeResult.Online);
    public void ObserveFailure(ConnectionProfile profile, Exception error)
    {
        if (Classify(error) is { } result) Observe(profile, result);
    }
    public static SiteProbeResult? Classify(Exception error)
    {
        if (error is AggregateException aggregate)
            return aggregate.InnerExceptions.Select(Classify).FirstOrDefault(result => result is not null);
        if (error is FtpCommandException ftp)
        {
            // User/host login limits are not evidence of an offline server.
            if (ftp.StatusCode is 421 or 530 && new[] { "too many", "maximum", "max users", "max connections", "user limit", "connection limit", "slots", "site is full" }.Any(word => ftp.Message.Contains(word, StringComparison.OrdinalIgnoreCase))) return SiteProbeResult.Busy;
            if (ftp.StatusCode == 530 && (ftp.Command is null || new[] { "USER", "PASS", "ACCT" }.Contains(ftp.Command.Split(' ', 2)[0], StringComparer.OrdinalIgnoreCase))) return SiteProbeResult.LoginFailed;
            return ftp.StatusCode == 421 ? SiteProbeResult.Unreachable : null;
        }
        if (error.InnerException is { } inner) return Classify(inner);
        if (error is AuthenticationException || error.GetType().Name.Contains("Authentication", StringComparison.Ordinal) || error.GetType().Name.Contains("HostKey", StringComparison.Ordinal)) return SiteProbeResult.LoginFailed;
        if (error is SocketException or TimeoutException or IOException) return SiteProbeResult.Unreachable;
        return null; // Cancellation, local slot waiting and file rejections never mark a site down.
    }
    private void Observe(ConnectionProfile profile, SiteProbeResult result)
    {
        SiteHealthSnapshot? update;
        lock (_gate)
        {
            if (_disposed || !_policy.Enabled || profile.Protocol == TransferProtocol.Sftp) return;
            if (!_sites.TryGetValue(profile.Id, out var entry)) _sites.Add(profile.Id, entry = new(profile, Now));
            if (entry.Profile != profile) return; // A finished transfer may still own an older saved login.
            entry.Revision++;
            update = Apply(entry, result);
        }
        Publish(update);
    }
    private SiteHealthSnapshot Apply(Entry entry, SiteProbeResult result)
    {
        var now = Now;
        if (result != SiteProbeResult.Deferred) entry.CheckedAt = now;
        if (result == SiteProbeResult.Online)
        { entry.State = SiteHealthState.Online; entry.Failures = 0; entry.LastFailure = null; entry.NextCheck = now.AddSeconds(_policy.CheckSeconds); }
        else if (result is SiteProbeResult.Deferred or SiteProbeResult.Busy)
        {
            if (result == SiteProbeResult.Busy && entry.Snapshot().Available) { entry.Failures = 0; entry.LastFailure = null; }
            entry.NextCheck = now.AddSeconds(entry.State == SiteHealthState.LoginFailed ? Math.Max(600, _policy.RetrySeconds) : _policy.RetrySeconds);
        }
        else
        {
            // Concurrent failures in the same connection burst count once.
            if (entry.LastFailure is null || now - entry.LastFailure.Value >= TimeSpan.FromSeconds(1)) { entry.Failures++; entry.LastFailure = now; }
            if (result == SiteProbeResult.LoginFailed) entry.State = SiteHealthState.LoginFailed;
            else if (entry.State != SiteHealthState.LoginFailed && entry.Failures >= _policy.Failures) entry.State = SiteHealthState.Offline;
            var delay = entry.State == SiteHealthState.LoginFailed ? Math.Max(600, _policy.RetrySeconds) : _policy.RetrySeconds;
            entry.NextCheck = now.AddSeconds(delay);
        }
        return entry.Snapshot();
    }
    private void Publish(SiteHealthSnapshot? update)
    {
        if (update is null) return;
        availability(update.Id, IsAvailable(update.Id)); changed(update);
    }
    public async Task CheckAsync(IEnumerable<ConnectionProfile> profiles, CancellationToken token, bool force = false)
    {
        if (force) await _runGate.WaitAsync(token).ConfigureAwait(false);
        else if (!await _runGate.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            ConnectionProfile[] due; int generation; SiteHealthPolicy policy; List<Guid> released;
            lock (_gate)
            {
                if (_disposed || !_policy.Enabled) return;
                var current = profiles.Where(profile => profile.Protocol != TransferProtocol.Sftp && (profile.EffectiveOptions.AllowDownload || profile.EffectiveOptions.AllowUpload)).DistinctBy(profile => profile.Id).ToArray();
                var ids = current.Select(profile => profile.Id).ToHashSet(); released = _sites.Keys.Where(id => !ids.Contains(id)).ToList();
                foreach (var id in released) _sites.Remove(id);
                foreach (var profile in current)
                {
                    if (!_sites.TryGetValue(profile.Id, out var entry)) _sites.Add(profile.Id, new(profile, Now));
                    else if (entry.Profile != profile) _sites[profile.Id] = new(profile, Now) { State = entry.State, Failures = entry.Failures, Revision = entry.Revision + 1, LastFailure = entry.LastFailure, CheckedAt = entry.CheckedAt };
                }
                policy = _policy; generation = _generation;
                due = _sites.Values.Where(entry => force || entry.NextCheck <= Now).OrderBy(entry => entry.NextCheck).Select(entry => entry.Profile).ToArray();
            }
            foreach (var id in released) availability(id, true);
            using var parallel = new SemaphoreSlim(2, 2);
            await Task.WhenAll(due.Select(async profile =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
                await parallel.WaitAsync(timeout.Token).ConfigureAwait(false);
                Entry? entry = null;
                try
                {
                    int revision;
                    lock (_gate)
                    {
                        if (_disposed || generation != _generation || !_sites.TryGetValue(profile.Id, out entry) || (!force && entry.NextCheck > Now)) return;
                        entry.Probing = true; revision = entry.Revision;
                    }
                    timeout.CancelAfter(TimeSpan.FromSeconds(policy.TimeoutSeconds));
                    SiteProbeResult result;
                    try { result = await probe(profile, timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested || _stop.IsCancellationRequested) { return; }
                    catch (OperationCanceledException) { result = SiteProbeResult.Unreachable; }
                    catch (Exception error) { result = Classify(error) ?? SiteProbeResult.Deferred; }
                    SiteHealthSnapshot? update = null;
                    lock (_gate)
                    {
                        if (generation == _generation && _sites.GetValueOrDefault(profile.Id) == entry && entry.Revision == revision && !token.IsCancellationRequested && !_stop.IsCancellationRequested)
                            update = Apply(entry, result);
                    }
                    Publish(update);
                }
                finally { lock (_gate) { if (entry is not null) entry.Probing = false; } parallel.Release(); }
            })).ConfigureAwait(false);
        }
        finally { _runGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _generation++; }
        _stop.Cancel(); await _runGate.WaitAsync().ConfigureAwait(false); _runGate.Release(); _stop.Dispose();
    }
}
