using System.IO;
using IoFtp.Core.Abstractions;

namespace IoFtp.Desktop.Services;

internal sealed record FxpProgressSample(long Bytes, long Speed);

internal sealed class FxpProgressProbe
{
    private readonly HashSet<string> _unsupported = new(StringComparer.OrdinalIgnoreCase);
    private long? _previousSize;
    private DateTimeOffset _previousAt;

    public async Task<FxpProgressSample?> ReadAsync(string name, string path, long total, bool upload,
        Func<string, CancellationToken, Task<RemoteCommandResult>> command,
        Func<CancellationToken, Task<long?>> size, CancellationToken token)
    {
        foreach (var verb in new[] { "SITE FLUXWHO", "SITE ioGuiExt who", "SITE WHO" })
        {
            if (_unsupported.Contains(verb)) continue;
            var reply = await command(verb, token);
            if (reply.StatusCode is 500 or 502 or 504 or 550) { _unsupported.Add(verb); continue; }
            if (reply.StatusCode == 421) throw new IOException("Monitoring connection closed by server.");
            if (reply.StatusCode is < 200 or >= 300) continue;
            long bytes = -1, speed;
            var matched = verb switch
            {
                "SITE FLUXWHO" => FxpSpeedParser.TryReadFluxWhoTransfer(reply.Message, name, upload, out bytes, out speed),
                "SITE ioGuiExt who" => FxpSpeedParser.TryReadIoFtpdTransfer(reply.Message, name, upload, out bytes, out speed),
                _ => FxpSpeedParser.TryReadDrFtpdTransfer(reply.Message, name, upload, out speed)
            };
            if (matched) { _previousSize = null; return new(bytes, speed); }
        }
        if (!upload) return null;
        var length = await size(token);
        if (length is null || FxpSpeedParser.IsPreallocatedSize(length.Value, total)) { _previousSize = null; return null; }
        var now = DateTimeOffset.UtcNow;
        FxpProgressSample? sample = null;
        if (_previousSize is { } previous && length >= previous)
            sample = new(length.Value, Math.Max(0, (long)((length.Value - previous) / Math.Max((now - _previousAt).TotalSeconds, .001))));
        _previousSize = length; _previousAt = now;
        return sample;
    }

    public void ResetConnection() { _previousSize = null; _unsupported.Clear(); }
}
