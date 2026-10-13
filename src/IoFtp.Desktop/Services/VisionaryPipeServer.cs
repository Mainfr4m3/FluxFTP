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
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;
    private readonly Func<ApiTransferRequest, VisionaryRaceState, CancellationToken, Task<bool>>? _raceStep;
    private readonly Dictionary<string, Task> _races = new(StringComparer.OrdinalIgnoreCase);
    private readonly RaceWatchSlots _watchSlots = new(32);

    public VisionaryPipeServer(Func<ApiTransferRequest, Task<object>> startTransfer, Action<string>? log = null,
        Func<ApiTransferRequest, VisionaryRaceState, CancellationToken, Task<bool>>? raceStep = null)
    {
        _startTransfer = startTransfer; _log = log; _raceStep = raceStep;
    }

    public void Start() => _listener = ListenAsync(_cancellation.Token);

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
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException exception) { _log?.Invoke($"Visionary pipe: {exception.Message}"); }
            catch (Exception exception) { _log?.Invoke($"Visionary pipe failed: {exception.Message}"); }
            if (!cancellationToken.IsCancellationRequested) await Task.Delay(25, cancellationToken);
        }
    }

    private async Task HandleAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        if (string.IsNullOrWhiteSpace(line)) { await writer.WriteLineAsync("ERROR Empty request"); return; }
        var received = RaceStartTrace.Now;
        var fields = line.Split('\t');
        if ((fields.Length != 5 && fields.Length != 7) || !fields[0].Equals("TRANSFER", StringComparison.OrdinalIgnoreCase) || fields.Skip(1).Any(string.IsNullOrWhiteSpace))
        { await writer.WriteLineAsync("ERROR Expected TRANSFER<TAB>section<TAB>release<TAB>source<TAB>target"); return; }
        try
        {
            if (fields[4] == "@DONE")
            {
                VisionaryTransferProfiles.ReleasePath(fields[2]);
                if (new[] { fields[1], fields[3] }.Any(value => value.Length > 128 || value.Contains('/') || value.Contains('\\') || value.Any(char.IsWhiteSpace)))
                    throw new ArgumentException("Invalid completion site or section.");
                RaceCompletionSignals.Shared.Record(fields[3], fields[1], fields[2]);
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
                if (_races.TryGetValue(key, out var active) && !active.IsCompleted)
                { await writer.WriteLineAsync("OK ALREADY WATCHING " + fields[2]); return; }
                if (_races.Any(pair => !pair.Value.IsCompleted && Overlapping(pair.Key, key)))
                    throw new InvalidOperationException("An overlapping release/subdirectory race already writes this destination. Use the existing watch.");
                foreach (var completed in _races.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray()) _races.Remove(completed);
                if (_races.Count >= 2048) throw new InvalidOperationException("VISIONARY backlog is full (2048 active/waiting watches). Try again after watches finish.");
                var options = VisionaryRaceOptions.Load();
                long chain = 0, dll = 0;
                if (fields.Length == 7) { long.TryParse(fields[5], out chain); long.TryParse(fields[6], out dll); }
                var state = new VisionaryRaceState(options) { StartTrace = new RaceStartTrace($"{fields[1]} {fields[2]}: {fields[3]} -> {fields[4]}", received, chain, dll, _log) };
                var request = new ApiTransferRequest(fields[3], null, fields[1], fields[4], null, fields[1], fields[2]);
                _races[key] = WatchAsync(request, state, options, cancellationToken);
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

    private async Task WatchAsync(ApiTransferRequest request, VisionaryRaceState state, VisionaryRaceOptions options, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(options.EffectiveTimeout);
        var label = $"VISIONARY {request.Name}: {request.SrcSite} -> {request.DstSite}";
        IDisposable? slot = null;
        _log?.Invoke(label + " accepted; waiting for a watch slot");
        try
        {
            state.StartTrace?.Mark("watch-slot-wait");
            slot = await _watchSlots.EnterAsync(lifetime.Token);
            state.StartTrace?.Mark("watch-admitted");
            _log?.Invoke(label + " watching source");
            while (!await _raceStep!(request, state, lifetime.Token))
                await Task.Delay(TimeSpan.FromSeconds(options.RefreshSeconds), lifetime.Token);
            _log?.Invoke(label + " COMPLETE: final transfers finished");
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke(label + (lifetime.IsCancellationRequested && !token.IsCancellationRequested
                ? $" stopped: maximum lifetime {options.EffectiveTimeout.TotalMinutes:0} min reached; removing pending jobs"
                : " stopped (cancelled or removed job)"));
        }
        catch (Exception ex) { _log?.Invoke(label + " FAILED: " + ex.Message); }
        finally
        {
            // Let the UI cancel this race's pending jobs even when cancellation occurred during the delay.
            state.StartTrace?.Mark("watch-ended");
            lifetime.Cancel();
            if (slot is not null)
            {
                try { await _raceStep!(request, state, lifetime.Token); } catch (Exception) { }
                slot.Dispose();
            }
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
