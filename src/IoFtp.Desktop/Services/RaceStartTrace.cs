using System.Runtime.InteropServices;

namespace IoFtp.Desktop.Services;

// All timestamps use the Windows boot clock shared with the native mIRC bridge.
internal sealed class RaceStartTrace
{
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
    public static long Now => checked((long)GetTickCount64());
    private readonly HashSet<string> _stages = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Action<string>? _log;
    private readonly Func<long> _clock;
    private readonly long _received;
    private readonly long? _chain;
    private readonly string _label;
    private readonly string _id = Guid.NewGuid().ToString("N")[..10];
    public RaceStartTrace(string label, long received, long chain, long dll, Action<string>? log, Func<long>? clock = null)
    {
        _label = label; _received = received; _log = log; _clock = clock ?? (() => Now);
        _chain = chain > 0 && chain <= received && received - chain <= 600000 ? chain : null;
        var dllValid = dll > 0 && dll <= received && received - dll <= 600000;
        Mark("pipe-received", $"chain-to-dll={(_chain.HasValue && dllValid && dll >= _chain.Value ? (dll - _chain.Value) + "ms" : "unknown")}; dll-to-pipe={(dllValid ? (received - dll) + "ms" : "unknown")}");
    }
    public void Mark(string stage, string detail = "")
    {
        var at = _clock();
        lock (_gate)
        {
            if (!_stages.Add(stage)) return;
            try { _log?.Invoke($"VISIONARY TIMING [{_id}] {_label} | {stage}; since-received={Math.Max(0, at - _received)}ms; since-chain={(_chain.HasValue ? Math.Max(0, at - _chain.Value) + "ms" : "unknown")}{(detail.Length == 0 ? "" : "; " + detail)}"); }
            catch { /* Diagnostics must never fail a transfer. */ }
        }
    }
    public void Protocol(string role, string message, string job)
    {
        if (message.StartsWith("> ", StringComparison.Ordinal))
        {
            var verb = message[2..].Split(' ', 2)[0];
            if (verb is "CWD" or "TYPE" or "PRET" or "CPSV" or "EPSV" or "PASV" or "SSCN" or "PORT" or "EPRT" or "STOR" or "RETR")
                Mark(role + "-" + verb, job);
        }
        else if (message.StartsWith("< 150", StringComparison.Ordinal) || message.StartsWith("< 125", StringComparison.Ordinal))
            Mark(role + "-preliminary-reply", job);
    }
}
