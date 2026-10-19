using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;

internal static class RaceListingChecks
{
    public static async Task Run()
    {
        var connections = new List<Fake>();
        await using var service = new RaceListingService((_, _) =>
        {
            var session = new Fake(); connections.Add(session);
            return Task.FromResult<IRemoteSession>(session);
        }, TimeSpan.FromMilliseconds(100));
        var profile = new ConnectionProfile(Guid.NewGuid(), "test", "example.invalid", 21, "user", TransferProtocol.Ftp);
        void Check(bool value, string message) { if (!value) throw new Exception("Race listing: " + message); }
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.ListAsync(profile, "/release", CancellationToken.None)));
        Check(connections.Count == 1 && connections[0].Lists == 1, "fan-out must share one login and one LIST");
        Check(results.All(result => ReferenceEquals(result, results[0])), "shared immutable snapshot");
        Check(await service.ReadSizeAsync(profile, "/release/not-in-list.rar", default) == 123,
            "direct SIZE finds a target omitted from the cached listing");
        Check(await service.ReadSizeAsync(profile, "/release/missing.rar", default) is null,
            "a removed target remains eligible for transfer");
        Check(connections.Count == 1 && connections[0].Lists == 1, "SIZE checks share the listing login without extra LIST or transfer workers");
        await Task.Delay(150);
        await service.ListAsync(profile, "/release", CancellationToken.None);
        Check(connections.Count == 1 && connections[0].Lists == 2, "refresh reuses connection");
        await service.ListAsync(profile, "/Release", CancellationToken.None);
        Check(connections[0].Lists == 3, "case-sensitive FTP paths remain distinct");
        await service.ListAsync(profile with { Password = "changed" }, "/release", CancellationToken.None);
        Check(connections.Count == 2 && connections[0].Disposed, "profile change invalidates connection and snapshots");
        connections[1].Fail = true;
        try { await service.ListAsync(profile with { Password = "changed" }, "/failed", CancellationToken.None); throw new Exception("Expected failure"); }
        catch (IOException) { }
        Check(connections[1].Disposed, "failed listing discards session");
        await service.ListAsync(profile with { Password = "changed" }, "/failed", CancellationToken.None);
        Check(connections.Count == 3, "next request reconnects and failure is not cached");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await service.ListAsync(profile, "/cancel", cancellation.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        Check(connections.Count == 3, "cancelled request opens no connection");
        await service.DisposeAsync();
        Check(connections.All(session => session.Disposed), "shutdown closes all sessions");
        var staleSessions = new List<Fake>();
        await using var stale = new RaceListingService((_, _) =>
        {
            var session = new Fake(); staleSessions.Add(session);
            return Task.FromResult<IRemoteSession>(session);
        });
        await stale.ListAsync(profile, "/one", default);
        staleSessions[0].AuthFailure = true;
        await stale.ListAsync(profile, "/two", default);
        Check(staleSessions.Count == 2 && staleSessions[0].Disposed, "530 on reused control session reconnects once and retries the listing");
        var deniedLogins = 0;
        await using var denied = new RaceListingService((_, _) =>
        {
            deniedLogins++;
            throw new FtpCommandException(530, "530 Access denied", "JBG", "PASS");
        });
        foreach (var path in new[] { "/one", "/two" })
        {
            try { await denied.ListAsync(profile, path, default); throw new Exception("Expected 530"); }
            catch (IOException ex) { Check(ex.Message.Contains("connect/login"), "login denial reports login phase"); }
        }
        Check(deniedLogins == 1, "fresh 530 login denial is shared as a cooldown rather than retried by every watch");
        try { await denied.ListAsync(profile with { Password = "corrected" }, "/one", default); }
        catch (IOException) { }
        Check(deniedLogins == 2, "changed login settings clear the cached denial immediately");
        Console.WriteLine("PASS: shared race listings, connection reuse, expiry, profile changes, failures and cancellation.");
    }

    private sealed class Fake : IRemoteSession
    {
        public bool Disposed, Fail, AuthFailure;
        public int Lists;
        public bool IsConnected => !Disposed;
        public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>();
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken token) => Task.CompletedTask;
        public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token)
        {
            Lists++; await Task.Delay(10, token);
            if (Fail) throw new IOException("fixture failure");
            if (AuthFailure) throw new FtpCommandException(530, "530 Not logged in", "JBG", "LIST " + path);
            return new[] { new RemoteEntry("file.rar", path + "/file.rar", false, Lists, null) };
        }
        public Task DownloadAsync(string path, Stream stream, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task UploadAsync(string path, Stream stream, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task<RemoteCommandResult> ExecuteCommandAsync(string command, CancellationToken token) =>
            Task.FromResult(command.EndsWith("/missing.rar") ? new RemoteCommandResult(550, "Not found") : new RemoteCommandResult(213, "213 123"));
        public Task DisconnectAsync(CancellationToken token) { Disposed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
