using System.Collections.Concurrent;
using System.Diagnostics;
using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

// One serialized source connection per site, shared by all destination watches.
internal sealed class RaceListingService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Source> _sources = new();
    private readonly Func<ConnectionProfile, CancellationToken, Task<IRemoteSession>> _connect;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _cleanup;
    private readonly TimeSpan _freshness;
    private sealed class Source
    {
        public int Invalidated;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly Dictionary<string, Snapshot> Listings = new(StringComparer.Ordinal);
        public ConnectionProfile? Profile;
        public IRemoteSession? Session;
        public long LastUsed = Stopwatch.GetTimestamp();
    }
    private sealed record Snapshot(long At, IReadOnlyList<RemoteEntry> Entries);

    public RaceListingService(Func<ConnectionProfile, CancellationToken, Task<IRemoteSession>> connect, TimeSpan? freshness = null)
    {
        _connect = connect;
        _freshness = freshness ?? TimeSpan.FromSeconds(1);
        _cleanup = CleanupAsync();
    }

    public void Invalidate(Guid site)
    {
        if (_sources.TryGetValue(site, out var source)) Interlocked.Exchange(ref source.Invalidated, 1);
    }
    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(ConnectionProfile profile, string path, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = lifetime.Token;
        token.ThrowIfCancellationRequested();
        var source = _sources.GetOrAdd(profile.Id, _ => new Source());
        await source.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref source.Invalidated, 0) != 0) source.Listings.Clear();
            if (source.Profile != profile)
            {
                await CloseAsync(source).ConfigureAwait(false);
                source.Profile = profile;
            }
            source.LastUsed = Stopwatch.GetTimestamp();
            if (source.Listings.TryGetValue(path, out var cached) && Stopwatch.GetElapsedTime(cached.At) < _freshness)
                return cached.Entries;
            try
            {
                if (source.Session?.IsConnected != true)
                {
                    await CloseAsync(source).ConfigureAwait(false);
                    source.Session = await _connect(profile, token).ConfigureAwait(false);
                }
                var entries = await source.Session.ListAsync(path, token).ConfigureAwait(false);
                // Publish only a fully successful listing. Destination filtering remains per watch.
                var snapshot = Array.AsReadOnly(entries.ToArray());
                foreach (var stale in source.Listings.Where(pair => Stopwatch.GetElapsedTime(pair.Value.At) >= _freshness).Select(pair => pair.Key).ToArray())
                    source.Listings.Remove(stale);
                if (source.Listings.Count >= 512) source.Listings.Clear();
                source.Listings[path] = new Snapshot(Stopwatch.GetTimestamp(), snapshot);
                return snapshot;
            }
            catch
            {
                await CloseAsync(source).ConfigureAwait(false);
                throw;
            }
        }
        finally { source.LastUsed = Stopwatch.GetTimestamp(); source.Gate.Release(); }
    }

    public async Task<long?> ReadSizeAsync(ConnectionProfile profile, string path, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = lifetime.Token;
        var source = _sources.GetOrAdd(profile.Id, _ => new Source());
        await source.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (source.Profile != profile)
            {
                await CloseAsync(source).ConfigureAwait(false);
                source.Profile = profile;
                source.Listings.Clear();
            }
            if (source.Session?.IsConnected != true)
            {
                await CloseAsync(source).ConfigureAwait(false);
                source.Session = await _connect(profile, token).ConfigureAwait(false);
            }
            var response = await source.Session.ExecuteCommandAsync("SIZE " + path, token).ConfigureAwait(false);
            var value = response.Message.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return response.StatusCode == 213 && long.TryParse(value, out var size) && size >= 0 ? size : null;
        }
        catch { await CloseAsync(source).ConfigureAwait(false); throw; }
        finally { source.LastUsed = Stopwatch.GetTimestamp(); source.Gate.Release(); }
    }

    public async Task<long?> TryReadTransferSizeAsync(ConnectionProfile profile, string path, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = lifetime.Token;
        if (!_sources.TryGetValue(profile.Id, out var source) || !await source.Gate.WaitAsync(0, token).ConfigureAwait(false)) return null;
        try
        {
            if (source.Profile != profile || source.Session?.IsConnected != true) return null;
            var slash = path.LastIndexOf('/');
            var parent = slash <= 0 ? "/" : path[..slash];
            var name = path[(slash + 1)..];
            if (Interlocked.Exchange(ref source.Invalidated, 0) != 0) source.Listings.Clear();
            if (!source.Listings.TryGetValue(parent, out var cached) || Stopwatch.GetElapsedTime(cached.At) >= _freshness)
            {
                try
                {
                    var entries = await source.Session.ListAsync(parent, token).ConfigureAwait(false);
                    cached = new Snapshot(Stopwatch.GetTimestamp(), Array.AsReadOnly(entries.ToArray()));
                    if (source.Listings.Count >= 512) source.Listings.Clear();
                    source.Listings[parent] = cached;
                }
                catch { await CloseAsync(source).ConfigureAwait(false); throw; }
            }
            return cached.Entries.FirstOrDefault(file => !file.IsDirectory && file.Name.Equals(name, StringComparison.Ordinal))?.Size;
        }
        finally { source.LastUsed = Stopwatch.GetTimestamp(); source.Gate.Release(); }
    }

    public async Task<RemoteCommandResult?> TryReadStatsAsync(ConnectionProfile profile, CancellationToken token, bool userDetails = false)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = lifetime.Token;
        // Diagnostics must not open additional logins or queue ahead of active race listings.
        if (!_sources.TryGetValue(profile.Id, out var source) || !await source.Gate.WaitAsync(0, token).ConfigureAwait(false)) return null;
        try
        {
            if (source.Profile != profile || source.Session?.IsConnected != true) return null;
            var username = source.Profile.Username;
            if (string.IsNullOrWhiteSpace(username) || username.Any(char.IsControl)) return null;
            try { return await source.Session.ExecuteCommandAsync((userDetails ? "SITE " : "SITE STATS ") + username, token).ConfigureAwait(false); }
            catch { await CloseAsync(source).ConfigureAwait(false); throw; }
        }
        finally { source.LastUsed = Stopwatch.GetTimestamp(); source.Gate.Release(); }
    }

    private static async Task CloseAsync(Source source)
    {
        var session = source.Session;
        source.Session = null;
        source.Listings.Clear();
        if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CleanupAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
                foreach (var source in _sources.Values)
                {
                    if (!await source.Gate.WaitAsync(0).ConfigureAwait(false)) continue;
                    try
                    {
                        var idle = Math.Clamp(source.Profile?.EffectiveOptions.MaxIdleSeconds ?? 30, 5, 30);
                        if (Stopwatch.GetElapsedTime(source.LastUsed) >= TimeSpan.FromSeconds(idle))
                            await CloseAsync(source).ConfigureAwait(false);
                    }
                    catch { /* Cleanup is best effort; the next listing reconnects. */ }
                    finally { source.Gate.Release(); }
                }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        await _cleanup.ConfigureAwait(false);
        foreach (var source in _sources.Values)
        {
            await source.Gate.WaitAsync().ConfigureAwait(false);
            try { await CloseAsync(source).ConfigureAwait(false); }
            finally { source.Gate.Release(); }
        }
    }
}
