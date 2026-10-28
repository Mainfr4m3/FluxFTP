using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;

internal static class SiteToolsChecks
{
 public static async Task Run()
 {
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
var sessions = new List<Fake>();
await using var service = new RaceListingService((_, _) => { var fake = new Fake(); sessions.Add(fake); return Task.FromResult<IRemoteSession>(fake); });
var a = new ConnectionProfile(Guid.NewGuid(), "A", "example.invalid", 21, "user", TransferProtocol.Ftp);
var b = a with { Id = Guid.NewGuid(), Name = "B" };
await service.ListAsync(a, "/one", default);
await service.ListAsync(b, "/one", default);
await service.DisconnectAsync(a.Id);
Check(sessions[0].Closed && !sessions[1].Closed, "Disconnect must close only the chosen site");
Check(!service.HasSession(a.Id) && service.HasSession(b.Id), "Connection state must reflect disconnect");
await service.ListAsync(a, "/one", default);
Check(sessions.Count == 3 && sessions[2].Lists == 1, "Next request must reconnect and refresh cached listing");
var current = sessions[2]; current.Block = true;
var listing = service.ListAsync(a, "/busy", default);
await current.Entered.Task;
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await service.DisconnectAsync(a.Id, cancelled.Token); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { }
Check(!current.Closed, "Cancelled disconnect must not close another operation's session");
var closing = service.DisconnectAsync(a.Id);
Check(!closing.IsCompleted && !current.Closed, "Disconnect must serialize with active listing");
current.Release.SetResult();
await listing; await closing;
Check(current.Closed && sessions.Count == 3, "Disconnect must close after active command without opening a new login");
await service.DisconnectAsync(Guid.NewGuid());
Console.WriteLine("PASS: chosen-site isolation, disconnect state, cache reset/reconnect, cancellation and serialization with active listing. No real sites contacted.");

 }
sealed class Fake : IRemoteSession {
 public bool Closed, Block; public int Lists;
 public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
 public bool IsConnected => !Closed;
 public IReadOnlySet<string> Capabilities {get;} = new HashSet<string>();
 public Task ConnectAsync(ConnectionProfile profile, CancellationToken token) => Task.CompletedTask;
 public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token) { Lists++; if (Block) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); } return Array.Empty<RemoteEntry>(); }
 public Task DownloadAsync(string p, Stream s, long o, IProgress<long>? r, CancellationToken t) => throw new NotSupportedException();
 public Task UploadAsync(string p, Stream s, long o, IProgress<long>? r, CancellationToken t) => throw new NotSupportedException();
 public Task<RemoteCommandResult> ExecuteCommandAsync(string c, CancellationToken t) => Task.FromResult(new RemoteCommandResult(200, "OK"));
 public Task DisconnectAsync(CancellationToken t) { Closed = true; return Task.CompletedTask; }
 public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
}

}
