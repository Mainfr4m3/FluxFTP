using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Text.Json;

namespace IoFtp.Desktop.Services;

internal sealed class VisionaryPipeServer : IAsyncDisposable
{
    internal const string PipeName = "FluxFTP.Visionary.v1";
    private readonly Func<ApiTransferRequest, Task<object>> _startTransfer;
    private readonly Action<string>? _log;
    private readonly Func<ApiTransferRequest, Task>? _liveSlots;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;
    private readonly Func<ApiTransferRequest, VisionaryRaceState, CancellationToken, Task<bool>>? _raceStep;
    private readonly Dictionary<string, Task> _races = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ApiTransferRequest> _requests = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, ApiTransferRequest>> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _notifications = new(StringComparer.OrdinalIgnoreCase);
    private void Notify(string section, string release)
    {
        foreach (var pair in _notifications)
            if (pair.Key.StartsWith(section + "\t" + release + "\t", StringComparison.OrdinalIgnoreCase) && pair.Value.CurrentCount == 0)
                try { pair.Value.Release(); } catch (SemaphoreFullException) { }
    }
    private readonly RaceWatchSlots _watchSlots;
    private int _recheckSeconds;
    private readonly Func<string, int> _sourcePriority;
    private readonly Action<ApiTransferRequest>? _raceObserved;
    private readonly Action<string, string, string>? _raceResult;
    private readonly Func<bool>? _sectionChainMode;
    private readonly Func<string, string[][], string[][]>? _planChains;
    private readonly Func<string, string, string, bool>? _routeAllowed;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _sourceOrder = new(StringComparer.OrdinalIgnoreCase);
    private long _arrival;
    private bool SectionChains => _sectionChainMode?.Invoke() == true;

    public VisionaryPipeServer(Func<ApiTransferRequest, Task<object>> startTransfer, Action<string>? log = null,
        Func<ApiTransferRequest, VisionaryRaceState, CancellationToken, Task<bool>>? raceStep = null,
        Func<ApiTransferRequest, Task>? liveSlots = null, int concurrentScheduling = 32, int? recheckSeconds = null,
        Func<string, int>? sourcePriority = null, Action<ApiTransferRequest>? raceObserved = null,
        Action<string, string, string>? raceResult = null, Func<bool>? sectionChainMode = null,
        Func<string, string[][], string[][]>? planChains = null, Func<string, string, string, bool>? routeAllowed = null)
    {
        _startTransfer = startTransfer; _log = log; _raceStep = raceStep; _liveSlots = liveSlots;
        _watchSlots = new RaceWatchSlots(Math.Clamp(concurrentScheduling, 1, 2048));
        _recheckSeconds = recheckSeconds ?? 0;
        _sourcePriority = sourcePriority ?? (_ => 0);
        _raceObserved = raceObserved;
        _raceResult = raceResult;
        _sectionChainMode = sectionChainMode; _planChains = planChains; _routeAllowed = routeAllowed;
    }

    public void Start() => _listener = ListenAsync(_cancellation.Token);
    public void UpdateScheduling(int concurrentScheduling, int? recheckSeconds)
    {
        _watchSlots.Configure(concurrentScheduling);
        Volatile.Write(ref _recheckSeconds, recheckSeconds is { } value ? Math.Clamp(value, 1, 60) : 0);
    }

    public async Task<string> SubmitChainsAsync(string section, string release, string pairs)
    {
        using var stream = new MemoryStream();
        var input = Encoding.UTF8.GetBytes($"CHAINS\t{section}\t{release}\t{pairs}\t0\t0\n");
        stream.Write(input); stream.Position = 0;
        await HandleAsync(stream, _cancellation.Token);
        return Encoding.UTF8.GetString(stream.ToArray(), input.Length, (int)stream.Length - input.Length).Trim();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleAsync(pipe, cancellationToken);
                continue; // Accept the next chain without an artificial 25 ms gap.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException exception) { _log?.Invoke($"Visionary pipe: {exception.Message}"); }
            catch (Exception exception) { _log?.Invoke($"Visionary pipe failed: {exception.Message}"); }
            if (!cancellationToken.IsCancellationRequested) await Task.Delay(25, cancellationToken);
        }
    }

    private Task HandleAsync(Stream stream, CancellationToken cancellationToken, long batchReceived = 0)
        => HandlePlannedAsync(stream, cancellationToken, batchReceived, false);

    private async Task HandlePlannedAsync(Stream stream, CancellationToken cancellationToken, long batchReceived, bool planned)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        if (string.IsNullOrWhiteSpace(line)) { await writer.WriteLineAsync("ERROR Empty request"); return; }
        var received = batchReceived > 0 ? batchReceived : RaceStartTrace.Now;
        var fields = line.Split('\t');
        if (fields.Length == 6 && fields[0] == "CHAINS")
        {
            var acknowledged = false;
            try
            {
                VisionaryTransferProfiles.ReleasePath(fields[2]);
                var pairs = fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split(';')).ToArray();
                if (pairs.Length is < 1 or > 256 || pairs.Any(pair => pair.Length is not (2 or 4) || pair.Take(2).Any(site => string.IsNullOrWhiteSpace(site) || site.Any(char.IsWhiteSpace) || site.Contains('/') || site.Contains('\\')) || (pair.Length == 4 && pair.Skip(2).Any(value => !ValidSlots(value)))))
                    throw new ArgumentException("Invalid chain batch (maximum 256 pairs).");
                var incomingCount = pairs.Length;
                var incomingRoutes = pairs.Select(pair => pair[0] + "\t" + pair[1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var sites = string.Join(",", pairs.SelectMany(pair => pair.Take(2)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(site => site, StringComparer.OrdinalIgnoreCase));
                _log?.Invoke($"VISIONARY CHAINS RECEIVED [{fields[1]}] {fields[2]}: incoming={incomingCount}; sites={sites}; section-chain-mode={SectionChains}");
                if (!planned && SectionChains && _planChains is not null)
                {
                    pairs = _planChains(fields[1], pairs);
                    _log?.Invoke($"VISIONARY section chains {fields[1]} / {fields[2]}: {pairs.Length} permitted routes; 1 Gbit -> 10 Gbit blocked");
                }
                var permitted = pairs.Select(pair => pair[0] + "\t" + pair[1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var filtered = incomingRoutes.Count(route => !permitted.Contains(route));
                var added = permitted.Count(route => !incomingRoutes.Contains(route));
                await writer.WriteLineAsync($"OK RECEIVED {pairs.Length} chain pairs; incoming={incomingCount}; filtered={filtered}; added={added}; per-pair results are in the FluxFTP log");
                acknowledged = true;
                foreach (var pair in pairs)
                {
                    var slots = pair.Length == 4 ? $"\t{pair[2]}\t{pair[3]}" : "";
                    var request = $"TRANSFER\t{fields[1]}\t{fields[2]}\t{pair[0]}\t{pair[1]}\t{fields[4]}\t{fields[5]}{slots}\n";
                    var input = Encoding.UTF8.GetBytes(request);
                    using var local = new MemoryStream();
                    local.Write(input); local.Position = 0;
                    await HandlePlannedAsync(local, cancellationToken, received, planned: true);
                    var result = Encoding.UTF8.GetString(local.ToArray(), input.Length, (int)local.Length - input.Length).Trim();
                    _log?.Invoke($"VISIONARY batch {pair[0]} -> {pair[1]}: {result}");
                }
            }
            catch (Exception exception)
            {
                if (!acknowledged) await writer.WriteLineAsync("ERROR " + exception.Message.Replace('\r', ' ').Replace('\n', ' '));
                _log?.Invoke("VISIONARY batch error: " + exception.Message);
            }
            return;
        }
        if ((fields.Length != 5 && fields.Length != 7 && fields.Length != 9) || !fields[0].Equals("TRANSFER", StringComparison.OrdinalIgnoreCase) || fields.Skip(1).Any(string.IsNullOrWhiteSpace) || (fields.Length == 9 && fields.Skip(7).Any(value => !ValidSlots(value))))
        { await writer.WriteLineAsync("ERROR Expected TRANSFER<TAB>section<TAB>release<TAB>source<TAB>target"); return; }
        if (!planned && SectionChains && _planChains is not null && !fields[4].StartsWith('@'))
        {
            try
            {
                VisionaryTransferProfiles.ReleasePath(fields[2]);
                var original = fields.Length == 9 ? new[] { fields[3], fields[4], fields[7], fields[8] } : new[] { fields[3], fields[4] };
                var pairs = _planChains(fields[1], [original]);
                foreach (var pair in pairs)
                {
                    var request = $"TRANSFER\t{fields[1]}\t{fields[2]}\t{pair[0]}\t{pair[1]}\t0\t0" + (pair.Length == 4 ? $"\t{pair[2]}\t{pair[3]}" : "") + "\n";
                    var input = Encoding.UTF8.GetBytes(request); using var local = new MemoryStream(); local.Write(input); local.Position = 0;
                    await HandlePlannedAsync(local, cancellationToken, received, planned: true);
                    _log?.Invoke(Encoding.UTF8.GetString(local.ToArray(), input.Length, (int)local.Length - input.Length).Trim());
                }
                await writer.WriteLineAsync($"OK RECEIVED {pairs.Length} permitted section routes");
            }
            catch (Exception error) { await writer.WriteLineAsync("ERROR " + error.Message.Replace('\r', ' ').Replace('\n', ' ')); }
            return;
        }
        try
        {
            if (fields[4].StartsWith("@RESULT:", StringComparison.Ordinal))
            {
                VisionaryTransferProfiles.ReleasePath(fields[2]);
                var winner = fields[4][8..];
                if (winner.Length is < 1 or > 128 || winner.Any(char.IsControl) || fields[3].Contains('/') || fields[3].Contains('\\'))
                    throw new ArgumentException("Invalid race-result site or winner.");
                _raceResult?.Invoke(fields[3], fields[2], winner);
                await writer.WriteLineAsync("OK RACE RESULT " + fields[2]);
                return;
            }
            if (fields[4] is "@DONE" or "@UPDATE")
            {
                VisionaryTransferProfiles.ReleasePath(fields[2]);
                if (new[] { fields[1], fields[3] }.Any(value => value.Length > 128 || value.Contains('/') || value.Contains('\\') || value.Any(char.IsWhiteSpace)))
                    throw new ArgumentException("Invalid completion site or section.");
                if (fields[4] == "@DONE") RaceCompletionSignals.Shared.Record(fields[3], fields[1], fields[2]);
                Notify(fields[1], fields[2]);
                _log?.Invoke($"VISIONARY DONE received: {fields[3]} / {fields[1]} / {fields[2]}; payload verification still required");
                await writer.WriteLineAsync("OK DONE RECORDED " + fields[2]);
                return;
            }
            if (_raceStep is not null)
            {
                if (new[] { fields[1], fields[3], fields[4] }.Any(value => value is "." or ".." || value.Contains('/') || value.Contains('\\') || value.Any(char.IsControl)))
                    throw new ArgumentException("Invalid section, release or site name.");
                VisionaryTransferProfiles.ReleasePath(fields[2]);
                // One writer per destination release, including when several sources announce it.
                var key = string.Join('\t', fields[1], fields[2], fields[4]);
                var incoming = new ApiTransferRequest(fields[3], null, fields[1], fields[4], null, fields[1], fields[2],
                    fields.Length == 9 && fields[7] != "-" ? int.Parse(fields[7]) : null,
                    fields.Length == 9 && fields[8] != "-" ? int.Parse(fields[8]) : null);
                if (_liveSlots is not null) await _liveSlots(incoming);
                _raceObserved?.Invoke(incoming);
                var sources = _sources.GetOrAdd(key, _ => new(StringComparer.OrdinalIgnoreCase));
                _sourceOrder.TryAdd(key + "\t" + incoming.SrcSite, Interlocked.Increment(ref _arrival));
                sources.AddOrUpdate(incoming.SrcSite!, incoming, (_, previous) => incoming with {
                    SourceDownloadSlots = incoming.SourceDownloadSlots ?? previous.SourceDownloadSlots,
                    DestinationUploadSlots = incoming.DestinationUploadSlots ?? previous.DestinationUploadSlots });
                if (_races.TryGetValue(key, out var active) && !active.IsCompleted)
                {
                    _requests.AddOrUpdate(key, incoming, (_, previous) => previous with {
                        SourceDownloadSlots = previous.SrcSite == incoming.SrcSite ? incoming.SourceDownloadSlots ?? previous.SourceDownloadSlots : previous.SourceDownloadSlots,
                        DestinationUploadSlots = incoming.DestinationUploadSlots ?? previous.DestinationUploadSlots });
                    Notify(fields[1], fields[2]); await writer.WriteLineAsync("OK WATCH UPDATED " + fields[2]); return;
                }
                if (_races.Any(pair => !pair.Value.IsCompleted && Overlapping(pair.Key, key)))
                    throw new InvalidOperationException("An overlapping release/subdirectory race already writes this destination. Use the existing watch.");
                foreach (var completed in _races.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray()) _races.Remove(completed);
                if (_races.Count >= 2048) throw new InvalidOperationException("VISIONARY backlog is full (2048 active/waiting watches). Try again after watches finish.");
                var options = VisionaryRaceOptions.Load();
                if (Volatile.Read(ref _recheckSeconds) is > 0 and var seconds) options = options with { RecheckSeconds = Math.Clamp(seconds, 1, 60) };
                long chain = 0, dll = 0;
                if (fields.Length >= 7) { long.TryParse(fields[5], out chain); long.TryParse(fields[6], out dll); }
                var state = new VisionaryRaceState(options) { StartTrace = new RaceStartTrace($"{fields[1]} {fields[2]}: {fields[3]} -> {fields[4]}", received, chain, dll, _log) };
                var request = incoming;
                _requests[key] = request;
                var notification = _notifications.GetOrAdd(key, _ => new SemaphoreSlim(0, 1));
                state.TransferCompleted = () =>
                {
                    state.StartTrace?.Mark("transfer-completed-wakeup", "fresh listing and verification requested");
                    if (notification.CurrentCount == 0) try { notification.Release(); } catch (SemaphoreFullException) { }
                };
                _races[key] = WatchAsync(request, state, options, cancellationToken, notification);
                await writer.WriteLineAsync("OK ACCEPTED " + fields[2] + " — queued or watching; progress is in the FluxFTP log");
                return;
            }
            var response = await _startTransfer(new(fields[3], null, fields[1], fields[4], null, fields[1], fields[2]));
            await writer.WriteLineAsync($"OK {fields[3]} -> {fields[4]} {fields[2]} {JsonSerializer.Serialize(response)}");
            _log?.Invoke($"Visionary queued {fields[1]} {fields[2]}: {fields[3]} -> {fields[4]}");
        }
        catch (Exception exception) { await writer.WriteLineAsync($"ERROR {exception.Message.Replace('\r', ' ').Replace('\n', ' ')}"); }
    }

    private static bool Overlapping(string left, string right)
    {
        var a = left.Split('\t'); var b = right.Split('\t');
        return a[0].Equals(b[0], StringComparison.OrdinalIgnoreCase) && a[2].Equals(b[2], StringComparison.OrdinalIgnoreCase) &&
            (a[1].StartsWith(b[1] + "/", StringComparison.OrdinalIgnoreCase) || b[1].StartsWith(a[1] + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidSlots(string value) => value == "-" ||
        (int.TryParse(value, out var slots) && slots is >= 0 and <= 1024);

    private async Task WatchAsync(ApiTransferRequest request, VisionaryRaceState state, VisionaryRaceOptions options, CancellationToken token, SemaphoreSlim notification)
    {
        await Task.Yield(); // Admit all pairs before beginning potentially expensive race work.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(options.EffectiveTimeout);
        var label = $"VISIONARY {request.Name}: {request.SrcSite} -> {request.DstSite}";
        var admitted = false;
        var sourceStates = new Dictionary<string, VisionaryRaceState>(StringComparer.OrdinalIgnoreCase) { [request.SrcSite!] = state };
        var owner = state.DestinationOwner;
        var completed = false;
        var sourceOffset = 0;
        var retrySources = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var skippedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedDestination = false;
        _log?.Invoke(label + " accepted; waiting for a watch slot");
        try
        {
            while (!completed)
            {
                var key = string.Join('\t', request.SrcSection, request.Name, request.DstSite);
                var available = _sources.TryGetValue(key, out var candidates)
                    ? candidates.Values.OrderBy(item => item.SrcSite, StringComparer.OrdinalIgnoreCase).ToArray()
                    : new[] { request };
                if (SectionChains)
                    available = available.Where(item => _routeAllowed?.Invoke(item.SrcSection!, item.SrcSite!, item.DstSite!) != false)
                        .OrderBy(item => _sourceOrder.GetValueOrDefault(key + "\t" + item.SrcSite)).ToArray();
                else available = available.Skip(sourceOffset).Concat(available.Take(sourceOffset))
                    .OrderByDescending(item => _sourcePriority(item.SrcSite!)).ToArray();
                // Rotate admission order so one source cannot reserve every file
                // before another source has had its next scheduling pass.
                for (var pass = 0; pass < 2 && !completed && !skippedDestination; pass++)
                for (var i = 0; i < available.Length && !skippedDestination; i++)
                {
                    var next = available[i];
                    if (skippedSources.Contains(next.SrcSite!)) continue;
                    if (retrySources.TryGetValue(next.SrcSite!, out var retryAt) && DateTimeOffset.UtcNow < retryAt) continue;
                    if (!sourceStates.TryGetValue(next.SrcSite!, out var nextState))
                    {
                        nextState = new VisionaryRaceState(options) {
                            DestinationOwner = owner, TransferCompleted = state.TransferCompleted,
                            StartTrace = new RaceStartTrace($"{next.SrcSection} {next.Name}: {next.SrcSite} -> {next.DstSite}", RaceStartTrace.Now, 0, 0, _log) };
                        sourceStates[next.SrcSite!] = nextState;
                        _log?.Invoke($"VISIONARY parallel source {request.Name}: {next.SrcSite} -> {next.DstSite}; shared destination file reservations");
                    }
                    state = nextState;
                    state.SourceCount = available.Length;
                    state.BorrowIdleSlots = pass == 1;
                    request = next;
                    if (_requests.TryGetValue(key, out var latest)) request = request with { DestinationUploadSlots = latest.DestinationUploadSlots };
                    state.StartTrace?.Mark("watch-slot-wait");
                    using (await _watchSlots.EnterAsync(lifetime.Token))
                    {
                    state.StartTrace?.Mark("watch-admitted");
                    if (!admitted) _log?.Invoke(label + $" watching source; recheck={options.EffectiveRecheckSeconds}s; maximum lifetime={options.EffectiveTimeout.TotalMinutes:0} min");
                    admitted = true;
                    try
                    {
                        if (await _raceStep!(request, state, lifetime.Token)) { completed = true; break; }
                    }
                    catch (Exception ex) when (!lifetime.IsCancellationRequested && ex is not OperationCanceledException &&
                        (state.DirectorySkip ?? LockedDirectorySkip.Match(ex, request.SrcSite, request.DstSite)) is not null)
                    {
                        var skip = state.DirectorySkip ?? LockedDirectorySkip.Match(ex, request.SrcSite, request.DstSite)!;
                        state.SkipDirectory(skip);
                        if (skip.Destination) skippedDestination = true;
                        else skippedSources.Add(request.SrcSite!);
                        _log?.Invoke($"VISIONARY SKIP [{skip.Site}] {request.Name}: {request.SrcSite} -> {request.DstSite}; directory locked (550); no retry for this race route");
                    }
                    catch (Exception ex) when (!lifetime.IsCancellationRequested && ex is not OperationCanceledException && !state.DirectoryDenied)
                    {
                        // RunVisionaryStep cleans this source's jobs before throwing.
                        // Do not terminate healthy alternatives for one source failure.
                        retrySources[request.SrcSite!] = DateTimeOffset.UtcNow.AddSeconds(30);
                        sourceStates.Remove(request.SrcSite!);
                        _log?.Invoke($"VISIONARY source FAILED {request.Name}: {request.SrcSite} -> {request.DstSite}: {ex.Message}; retry in 30s; other sources continue");
                    }
                    }
                }
                sourceOffset = available.Length == 0 ? 0 : (sourceOffset + 1) % available.Length;
                if (completed || skippedDestination) break;
                // Incoming Visi updates wake immediately; periodic checks also recover
                // locks and growing sources when the bots send no further announcement.
                var currentRecheck = Volatile.Read(ref _recheckSeconds);
                await notification.WaitAsync(TimeSpan.FromSeconds(currentRecheck > 0 ? currentRecheck : options.EffectiveRecheckSeconds), lifetime.Token);
            }
            if (!skippedDestination)
                _log?.Invoke($"VISIONARY {request.Name}: {request.SrcSite} -> {request.DstSite} COMPLETE: final transfers finished");
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke(label + (lifetime.IsCancellationRequested && !token.IsCancellationRequested
                ? $" stopped: maximum lifetime {options.EffectiveTimeout.TotalMinutes:0} min reached; removing pending jobs"
                : " stopped (cancelled or removed job)"));
        }
        catch (Exception) when (lifetime.IsCancellationRequested)
        {
            _log?.Invoke(label + (token.IsCancellationRequested ? " stopped (cancelled or removed job)" :
                $" stopped: maximum lifetime {options.EffectiveTimeout.TotalMinutes:0} min reached; removing pending jobs"));
        }
        catch (Exception ex) { _log?.Invoke(label + " FAILED: " + ex.Message); }
        finally
        {
            var key = string.Join('\t', request.SrcSection, request.Name, request.DstSite);
            _requests.TryRemove(key, out _);
            _sources.TryRemove(key, out _);
            foreach (var sourceKey in _sourceOrder.Keys.Where(k => k.StartsWith(key + "\t", StringComparison.OrdinalIgnoreCase))) _sourceOrder.TryRemove(sourceKey, out _);
            // Let the UI cancel this race's pending jobs even when cancellation occurred during the delay.
            state.StartTrace?.Mark("watch-ended");
            lifetime.Cancel();
            foreach (var saved in sourceStates.Values) saved.TransferCompleted = null;
            if (admitted)
            {
                foreach (var pair in sourceStates)
                {
                    try { await _raceStep!(request with { SrcSite = pair.Key }, pair.Value, lifetime.Token); } catch (Exception) { }
                }
            }
            RaceDestinationLease.Release(owner);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_listener is not null) try { await _listener; } catch (OperationCanceledException) { }
        await Task.WhenAll(_races.Values);
        _watchSlots.Dispose();
        _cancellation.Dispose();
    }
}
