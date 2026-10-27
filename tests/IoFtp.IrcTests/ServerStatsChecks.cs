using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;
using IoFtp.Desktop.Services;

internal static class ServerStatsChecks
{
    public static async Task Run()
    {
        var response = "226-\u001b[32mUserTop\u001b[0m\r\n226- 1. TestUser 100 MB\r\n226 UL: 281 TB | DL: 4 TB | Credits: 10 MB";
        var router = new SiteCommandLogRouter();
        if (!router.Route("> STAT", "STAT") || !router.Route("< 211-Weekly statistics", "STAT") ||
            !router.Route("< 211 Done", "STAT") || router.Route("> RETR file.rar", "STAT"))
            throw new Exception("Configured non-SITE status command/reply routing failed");
        var report = ServerTransferReport.Statistics(response);
        if (report is null || report.Contains('\u001b') || report.Contains("226-") || !report.Contains("TestUser 100 MB") || !report.Contains("\r\n")) throw new Exception("Server table formatting or user stats lost");
        if (ServerTransferReport.Statistics("226 Transfer complete") is not null) throw new Exception("Generic completion is not user statistics");
        if (ServerTransferReport.Statistics("226 Credits: " + new string('x', 100_000))!.Length > 65_536) throw new Exception("Unbounded report");
        if (!VisionaryLogVisibility.Show("SERVER STATS UserTop failed_user", false)) throw new Exception("User statistics must remain visible without Debug");
        if (VisionaryLogVisibility.Show("SERVER DETAILS CRC-Check: Failed", false) || !VisionaryLogVisibility.Show("SERVER DETAILS CRC-Check: Failed", true)) throw new Exception("Server detail must require Debug");
        var profile = new ConnectionProfile(Guid.NewGuid(), "TEST", "example.invalid", 21, "user", TransferProtocol.Ftp);
        var session = new FakeSession();
        var logins = 0;
        var now = DateTimeOffset.Parse("2026-10-27T10:00:00Z");
        await using var listings = new RaceListingService((_, _) => { logins++; return Task.FromResult<IRemoteSession>(session); }, utcNow: () => now);
        if (await listings.TryReadStatsAsync(profile, default) is not null || logins != 0) throw new Exception("Statistics opened a new login");
        if (await listings.TryCheckHealthAsync(profile, default) is not null || logins != 0) throw new Exception("Shared health check opened a new login");
        if (await listings.TryReadTransferSizeAsync(profile, "/release/file.rar", default) is not null || logins != 0) throw new Exception("Progress sampling opened a new login");
        await listings.ListAsync(profile, "/release", default);
        if (await listings.TryReadTransferSizeAsync(profile, "/release/file.rar", default) != 500 || session.Lists != 1) throw new Exception("Progress sampling did not share cached directory listing");
        if ((await listings.TryReadStatsAsync(profile, default))?.StatusCode != 200 || session.Commands != 1 || logins != 1) throw new Exception("SITE WKUP 50 did not reuse existing control session");
        if (await listings.TryReadStatsAsync(profile, default, userDetails: true) is not null || session.Commands != 1 || session.LastCommand != "SITE WKUP 50" || logins != 1)
            throw new Exception("Disabled SITE USER diagnostics must not issue a command or open a login");
        now = now.AddMinutes(59).AddSeconds(59);
        if (await listings.TryReadStatsAsync(profile, default) is not null || session.Commands != 1)
            throw new Exception("Repeated refresh sent WKUP before one hour elapsed");
        session.Block = true;
        var busy = listings.ListAsync(profile, "/other", default);
        await session.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (await listings.TryReadTransferSizeAsync(profile, "/other/file.rar", default) is not null) throw new Exception("Progress sampling interrupted a busy listing");
        if (await listings.TryReadStatsAsync(profile, default) is not null || session.Commands != 1) throw new Exception("Statistics interrupted an active listing");
        if (await listings.TryCheckHealthAsync(profile, default) is not null || session.Commands != 1) throw new Exception("Health check interrupted an active listing");
        session.Release.TrySetResult(); await busy;
        session.Unsupported = true;
        session.Commands = 0;
        if (await listings.TryReadStatsAsync(profile, default, userDetails: true) is not null || session.Commands != 0) throw new Exception("Disabled SITE USER must not attempt fallback commands");
        now = now.AddSeconds(1);
        if ((await listings.TryReadStatsAsync(profile, default))?.StatusCode != 502 || session.Commands != 1 || !session.IsConnected) throw new Exception("Unsupported optional statistics closed a healthy session");
        if (await listings.TryReadStatsAsync(profile, default) is not null || session.Commands != 1)
            throw new Exception("Rejected WKUP was retried within the hour");
        session.Unsupported = false;
        now = now.AddHours(1);
        if ((await listings.TryReadStatsAsync(profile, default))?.StatusCode != 200 || session.Commands != 2)
            throw new Exception("WKUP did not become due after one hour");
        if ((await listings.TryReadStatsAsync(profile, default, command: "SITE WKUP 10", intervalMinutes: 5))?.StatusCode != 200 || session.LastCommand != "SITE WKUP 10")
            throw new Exception("Changing the status command did not apply immediately");
        now = now.AddMinutes(4).AddSeconds(59);
        if (await listings.TryReadStatsAsync(profile, default, command: "SITE WKUP 10", intervalMinutes: 5) is not null)
            throw new Exception("Custom interval sent the command early");
        now = now.AddSeconds(1);
        if ((await listings.TryReadStatsAsync(profile, default, command: "SITE WKUP 10", intervalMinutes: 5))?.StatusCode != 200)
            throw new Exception("Custom interval did not repeat the command");
        now = now.AddMinutes(2);
        if ((await listings.TryReadStatsAsync(profile, default, command: "SITE WKUP 10", intervalMinutes: 2))?.StatusCode != 200)
            throw new Exception("Shortening the interval did not apply to the last request");
        var oldSettings = System.Text.Json.JsonSerializer.Deserialize<IoFtp.Desktop.Models.GlobalSettings>("{}")!;
        if (oldSettings.StatusCommand != "SITE WKUP 50" || oldSettings.StatusCommandIntervalMinutes != 60)
            throw new Exception("Existing settings did not inherit hourly WKUP defaults");
        var customSettings = oldSettings with { StatusCommand = "SITE WKUP 10", StatusCommandIntervalMinutes = 5 };
        var restored = System.Text.Json.JsonSerializer.Deserialize<IoFtp.Desktop.Models.GlobalSettings>(System.Text.Json.JsonSerializer.Serialize(customSettings))!;
        if (restored.StatusCommand != customSettings.StatusCommand || restored.StatusCommandIntervalMinutes != 5)
            throw new Exception("Status command settings did not round-trip");
        foreach (var invalid in new[] { ("", 60), ("SITE WKUP 50\r\nQUIT", 60), ("SITE WKUP 50", 0), ("SITE WKUP 50", 10081) })
        {
            try { SiteStatusCommand.Validate(invalid.Item1, invalid.Item2); throw new Exception("Invalid status command settings accepted"); }
            catch (ArgumentException) { }
        }
        if (await listings.TryCheckHealthAsync(profile, default) != true || session.LastCommand != "NOOP" || logins != 1 || !listings.HasSession(profile.Id)) throw new Exception("Health check did not reuse shared control login with NOOP");
        session.Unsupported = true;
        if (await listings.TryCheckHealthAsync(profile, default) != false || listings.HasSession(profile.Id)) throw new Exception("Failed NOOP did not retire the stale shared connection");
        Console.WriteLine("PASS: server report formatting, bounded output, Debug visibility, existing-login reuse, busy-session skip and unsupported SITE WKUP 50.");
    }
    private sealed class FakeSession : IRemoteSession
    {
        public bool IsConnected { get; private set; } = true;
        public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>();
        public bool Block, Unsupported;
        public int Commands;
        public int Lists;
        public string? LastCommand;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken token) => Task.CompletedTask;
        public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token)
        {
            Lists++;
            if (Block) { Started.TrySetResult(); await Release.Task.WaitAsync(token); }
            return [new("file.rar", path + "/file.rar", false, 500, null)];
        }
        public Task<RemoteCommandResult> ExecuteCommandAsync(string command, CancellationToken token)
        {
            if (command is not ("SITE WKUP 50" or "SITE WKUP 10" or "NOOP")) throw new Exception("Only configured status and NOOP commands may be sent");
            LastCommand = command;
            Commands++; return Task.FromResult(new RemoteCommandResult(Unsupported ? 502 : 200, "UserTop: TestUser"));
        }
        public Task DownloadAsync(string path, Stream output, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task UploadAsync(string path, Stream input, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken token) { IsConnected = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { IsConnected = false; return ValueTask.CompletedTask; }
    }
}
