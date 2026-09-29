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

    public VisionaryPipeServer(Func<ApiTransferRequest, Task<object>> startTransfer, Action<string>? log = null)
    {
        _startTransfer = startTransfer; _log = log;
    }

    public void Start() => _listener = ListenAsync(_cancellation.Token);

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(cancellationToken); await HandleAsync(pipe, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException exception) { _log?.Invoke($"Visionary pipe: {exception.Message}"); }
            catch (Exception exception) { _log?.Invoke($"Visionary pipe failed: {exception.Message}"); }
        }
    }

    private async Task HandleAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line)) { await writer.WriteLineAsync("ERROR Empty request"); return; }
        var fields = line.Split('\t');
        if (fields.Length != 5 || !fields[0].Equals("TRANSFER", StringComparison.OrdinalIgnoreCase) || fields.Skip(1).Any(string.IsNullOrWhiteSpace))
        { await writer.WriteLineAsync("ERROR Expected TRANSFER<TAB>section<TAB>release<TAB>source<TAB>target"); return; }
        try
        {
            var response = await _startTransfer(new(fields[3], null, fields[1], fields[4], null, fields[1], fields[2]));
            await writer.WriteLineAsync($"OK {fields[3]} -> {fields[4]} {fields[2]} {JsonSerializer.Serialize(response)}");
            _log?.Invoke($"Visionary queued {fields[1]} {fields[2]}: {fields[3]} -> {fields[4]}");
        }
        catch (Exception exception) { await writer.WriteLineAsync($"ERROR {exception.Message.Replace('\r', ' ').Replace('\n', ' ')}"); }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_listener is not null) try { await _listener; } catch (OperationCanceledException) { }
        _cancellation.Dispose();
    }
}
