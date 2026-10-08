using IoFtp.Core.Abstractions;
using IoFtp.Desktop.Services;

internal static class FxpProgressProbeChecks
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var probe = new FxpProgressProbe();
        var commands = new List<string>();
        var row = "200-fluxwho|1|test|1|1024|123|RETR target.rar|/release/target.rar|/disk/target.rar|1|127.0.0.1|127.0.0.2";
        var missing = false;
        Task<RemoteCommandResult> Command(string verb, CancellationToken token)
        {
            commands.Add(verb);
            return Task.FromResult(verb == "SITE FLUXWHO" ? new RemoteCommandResult(502, "unsupported") :
                verb == "SITE ioGuiExt who" ? new RemoteCommandResult(550, "permission denied") :
                new RemoteCommandResult(200, missing ? "no transfer" : "-> DN 1MB/s to user - target.rar"));
        }
        Task<long?> NoSize(CancellationToken token) => throw new Exception("Source must not poll SIZE");
        var sample = await probe.ReadAsync("target.rar", "/target.rar", 1000, false, Command, NoSize, default);
        Check(sample?.Speed == 1000000 && sample.Bytes == -1, "WHO speed without fabricated progress");
        commands.Clear();
        await probe.ReadAsync("target.rar", "/target.rar", 1000, false, Command, NoSize, default);
        Check(commands.SequenceEqual(new[] { "SITE WHO" }), "unsupported and denied commands skipped on later polls");
        missing = true;
        Check(await probe.ReadAsync("target.rar", "/target.rar", 1000, false, Command, NoSize, default) is null, "missing row is not a zero-speed sample");
        probe.ResetConnection(); commands.Clear(); missing = false;
        await probe.ReadAsync("target.rar", "/target.rar", 1000, false, Command, NoSize, default);
        Check(commands.Count == 3, "new connection rechecks server capabilities");
        var zero = new FxpProgressProbe();
        sample = await zero.ReadAsync("target.rar", "/target.rar", 1000, false,
            (_, _) => Task.FromResult(new RemoteCommandResult(200, row.Replace("|1024|", "|0|"))), NoSize, default);
        Check(sample?.Speed == 0 && sample.Bytes == 123, "genuine zero-speed sample retains server byte counter");
        var sizing = new FxpProgressProbe();
        long? length = 1000;
        Task<RemoteCommandResult> Unsupported(string verb, CancellationToken token) => Task.FromResult(new RemoteCommandResult(502, "unsupported"));
        Task<long?> Size(CancellationToken token) => Task.FromResult(length);
        Check(await sizing.ReadAsync("target.rar", "/target.rar", 1000, true, Unsupported, Size, default) is null, "preallocated full-size file does not imply completion");
        length = 100;
        Check(await sizing.ReadAsync("target.rar", "/target.rar", 1000, true, Unsupported, Size, default) is null, "first SIZE establishes baseline");
        await Task.Delay(20); length = 200;
        sample = await sizing.ReadAsync("target.rar", "/target.rar", 1000, true, Unsupported, Size, default);
        Check(sample?.Bytes == 200 && sample.Speed > 0, "growing SIZE yields observed rate");
        sizing.ResetConnection();
        Check(await sizing.ReadAsync("target.rar", "/target.rar", 1000, true, Unsupported, Size, default) is null, "reconnect resets SIZE baseline");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var timedOut = false;
        try
        {
            await new FxpProgressProbe().ReadAsync("target.rar", "/target.rar", 1000, false,
                async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(200, row); }, NoSize, timeout.Token);
        }
        catch (OperationCanceledException) { timedOut = true; }
        Check(timedOut, "stalled command honors monitoring timeout");
        probe.ResetConnection();
        sample = await probe.ReadAsync("target.rar", "/target.rar", 1000, false,
            (_, _) => Task.FromResult(new RemoteCommandResult(200, row)), NoSize, default);
        Check(sample?.Speed == 1048576 && sample.Bytes == 123, "live measurement resumes after reconnect");
        Console.WriteLine($"PASS: {checks} FXP monitoring timeout, capability, dropout, reconnect and SIZE checks.");
    }
}
