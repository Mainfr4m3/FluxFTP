using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;
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
        Console.WriteLine("PASS: shared race listings, connection reuse, expiry, profile changes, failures and cancellation.");
    }

    private sealed class Fake : IRemoteSession
    {
        public bool Disposed, Fail;
        public int Lists;
        public bool IsConnected => !Disposed;
        public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>();
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken token) => Task.CompletedTask;
        public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token)
        {
            Lists++; await Task.Delay(10, token);
            if (Fail) throw new IOException("fixture failure");
            return new[] { new RemoteEntry("file.rar", path + "/file.rar", false, Lists, null) };
        }
        public Task DownloadAsync(string path, Stream stream, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task UploadAsync(string path, Stream stream, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task<RemoteCommandResult> ExecuteCommandAsync(string command, CancellationToken token) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken token) { Disposed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
