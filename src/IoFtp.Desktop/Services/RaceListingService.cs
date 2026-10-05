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
