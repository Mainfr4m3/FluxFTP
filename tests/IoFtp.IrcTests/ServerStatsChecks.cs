using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;
using IoFtp.Desktop.Services;

internal static class ServerStatsChecks
{
    public static async Task Run()
    {
        var response = "226-\u001b[32mUserTop\u001b[0m\r\n226- 1. TestUser 100 MB\r\n226 UL: 281 TB | DL: 4 TB | Credits: 10 MB";
        var report = ServerTransferReport.Statistics(response);
        if (report is null || report.Contains('\u001b') || report.Contains("226-") || !report.Contains("TestUser 100 MB") || !report.Contains("\r\n")) throw new Exception("Server table formatting or user stats lost");
        if (ServerTransferReport.Statistics("226 Transfer complete") is not null) throw new Exception("Generic completion is not user statistics");
        if (ServerTransferReport.Statistics("226 Credits: " + new string('x', 100_000))!.Length > 65_536) throw new Exception("Unbounded report");
        if (!VisionaryLogVisibility.Show("SERVER STATS UserTop failed_user", false)) throw new Exception("User statistics must remain visible without Debug");
        if (VisionaryLogVisibility.Show("SERVER DETAILS CRC-Check: Failed", false) || !VisionaryLogVisibility.Show("SERVER DETAILS CRC-Check: Failed", true)) throw new Exception("Server detail must require Debug");
        var profile = new ConnectionProfile(Guid.NewGuid(), "TEST", "example.invalid", 21, "user", TransferProtocol.Ftp);
        var session = new FakeSession();
        var logins = 0;
        await using var listings = new RaceListingService((_, _) => { logins++; return Task.FromResult<IRemoteSession>(session); });
        if (await listings.TryReadStatsAsync(profile, default) is not null || logins != 0) throw new Exception("Statistics opened a new login");
        if (await listings.TryReadTransferSizeAsync(profile, "/release/file.rar", default) is not null || logins != 0) throw new Exception("Progress sampling opened a new login");
        await listings.ListAsync(profile, "/release", default);
        if (await listings.TryReadTransferSizeAsync(profile, "/release/file.rar", default) != 500 || session.Lists != 1) throw new Exception("Progress sampling did not share cached directory listing");
        if ((await listings.TryReadStatsAsync(profile, default))?.StatusCode != 200 || session.Commands != 1 || logins != 1) throw new Exception("SITE STATS did not reuse existing control session");
        if ((await listings.TryReadStatsAsync(profile, default, weekly: true))?.StatusCode != 200 || session.Commands != 2 || session.LastCommand != "SITE WKUP user" || logins != 1)
            throw new Exception("Weekly statistics must reuse the logged-in session and explicitly request that user");
        session.Block = true;
        var busy = listings.ListAsync(profile, "/other", default);
        await session.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (await listings.TryReadTransferSizeAsync(profile, "/other/file.rar", default) is not null) throw new Exception("Progress sampling interrupted a busy listing");
        if (await listings.TryReadStatsAsync(profile, default) is not null || session.Commands != 2) throw new Exception("Statistics interrupted an active listing");
        session.Release.TrySetResult(); await busy;
        session.Unsupported = true;
        if ((await listings.TryReadStatsAsync(profile, default))?.StatusCode != 502 || !session.IsConnected) throw new Exception("Unsupported optional statistics closed a healthy session");
        Console.WriteLine("PASS: server report formatting, bounded output, Debug visibility, existing-login reuse, busy-session skip and unsupported SITE STATS.");
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
            if (command is not ("SITE STATS user" or "SITE WKUP user")) throw new Exception("Statistics must explicitly query the session's logged-in user");
            LastCommand = command;
            Commands++; return Task.FromResult(new RemoteCommandResult(Unsupported ? 502 : 200, "UserTop: TestUser"));
        }
        public Task DownloadAsync(string path, Stream output, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task UploadAsync(string path, Stream input, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken token) { IsConnected = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { IsConnected = false; return ValueTask.CompletedTask; }
    }
}
