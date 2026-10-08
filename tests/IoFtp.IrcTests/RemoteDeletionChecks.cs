using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;

internal static class RemoteDeletionChecks
{
    public static async Task Run()
    {
        var session = new Fake();
        await RemoteDeletion.DeleteAsync(session, "/release", true, true, default);
        if (!session.Commands.SequenceEqual(new[] { "DELE /release/file.bin", "DELE /release/Sub/nested.bin", "CWD /release", "RMD /release/Sub", "CWD /", "RMD /release" }))
            throw new Exception("Recursive deletion must leave folders before removing them.");
        var denied = new Fake { Deny = true };
        try { await RemoteDeletion.DeleteAsync(denied, "/release", true, true, default); throw new Exception("Ignored deletion rejection"); }
        catch (FtpCommandException error) when (error.StatusCode == 550 && error.Message.Contains("permission denied")) { }
        if (denied.Commands.Count != 1) throw new Exception("Deletion must stop at rejected file.");
        var sftp = new Fake();
        await RemoteDeletion.DeleteAsync(sftp, "/release/Sub", true, false, default);
        if (sftp.Commands.Any(command => command.StartsWith("CWD"))) throw new Exception("SFTP must not send CWD.");
        var file = new Fake();
        await RemoteDeletion.DeleteAsync(file, "/file with spaces.bin", false, true, default);
        if (file.Commands.Single() != "DELE /file with spaces.bin") throw new Exception("File path changed.");
        try
        {
            await RemoteDeletion.DeleteAsync(new Fake { RecreatedPath = "/release/Sub" }, "/release/Sub", true, true, default);
            throw new Exception("Unverified server deletion accepted");
        }
        catch (System.IO.IOException error) when (error.Message.Contains("still present")) { }
        foreach (var path in new[] { "/", "/release/../other", "/release\r\nDELE /other" })
        {
            var guarded = new Fake();
            try { await RemoteDeletion.DeleteAsync(guarded, path, true, true, default); throw new Exception("Unsafe deletion accepted"); }
            catch (System.IO.IOException) { }
            if (guarded.Commands.Count != 0) throw new Exception("Invalid path reached server.");
        }
        Console.WriteLine("PASS: recursive FTP deletion with CWD changes, rejection handling, SFTP, spaces and path guards.");
    }
    private sealed class Fake : IRemoteSession
    {
        public List<string> Commands { get; } = [];
        public bool Deny { get; init; }
        public string? RecreatedPath { get; init; }
        private string _cwd = "/";
        private readonly HashSet<string> _removed = [];
        public bool IsConnected => true;
        public IReadOnlySet<string> Capabilities => new HashSet<string>();
        public Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token)
        {
            _cwd = path;
            IReadOnlyList<RemoteEntry> entries = path == "/release" ?
                [new("file.bin", path + "/file.bin", false, 1, null), new("Sub", path + "/Sub", true, null, null)] :
                [new("nested.bin", path + "/nested.bin", false, 1, null)];
            return Task.FromResult<IReadOnlyList<RemoteEntry>>(entries.Where(entry => !_removed.Contains(entry.FullPath) || entry.FullPath == RecreatedPath).ToArray());
        }
        public Task<RemoteCommandResult> ExecuteCommandAsync(string command, CancellationToken token)
        {
            Commands.Add(command);
            if (Deny) return Task.FromResult(new RemoteCommandResult(550, "permission denied"));
            if (command.StartsWith("CWD ")) _cwd = command[4..];
            if (command == "RMD " + _cwd && Commands.Any(item => item.StartsWith("CWD ")))
                return Task.FromResult(new RemoteCommandResult(550, "cannot remove current directory"));
            if (command.StartsWith("DELE ")) _removed.Add(command[5..]);
            if (command.StartsWith("RMD ")) _removed.Add(command[4..]);
            return Task.FromResult(new RemoteCommandResult(250, "OK"));
        }
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken token) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task DownloadAsync(string path, Stream stream, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task UploadAsync(string path, Stream stream, long offset, IProgress<long>? progress, CancellationToken token) => throw new NotSupportedException();
    }
}
