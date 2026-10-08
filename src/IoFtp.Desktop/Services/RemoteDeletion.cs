using System.IO;
using IoFtp.Core.Abstractions;
using IoFtp.Core.Transport;

namespace IoFtp.Desktop.Services;

internal static class RemoteDeletion
{
    public static async Task DeleteAsync(IRemoteSession session, string path, bool directory, bool ftp, CancellationToken token, Action<string>? report = null)
    {
        path = Validate(path);
        async Task Checked(string command)
        {
            var result = await session.ExecuteCommandAsync(command, token);
            if (result.StatusCode != 250)
                throw new FtpCommandException(result.StatusCode, $"{command}: {result.StatusCode} {result.Message}");
        }
        async Task Verify()
        {
            var split = path.LastIndexOf('/');
            var parent = split == 0 ? "/" : path[..split];
            var name = path[(split + 1)..];
            if ((await session.ListAsync(parent, token)).Any(entry => entry.Name.Equals(name, StringComparison.Ordinal)))
                throw new IOException($"Server accepted deletion, but '{path}' is still present in a fresh listing. It may have been recreated by another application.");
            report?.Invoke($"Delete verified: {path} is absent from {parent}.");
        }
        if (!directory) { await Checked($"DELE {path}"); await Verify(); return; }
        var pending = new Stack<string>(); var directories = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var folder = pending.Pop(); directories.Push(folder);
            var children = await session.ListAsync(folder, token);
            report?.Invoke($"Delete listing: {folder}: {children.Count(entry => !entry.IsDirectory)} files, {children.Count(entry => entry.IsDirectory)} folders.");
            foreach (var child in children)
            {
                if (child.Name is "." or "..") continue;
                if (child.Name.Contains('/') || child.Name.Contains('\\') || child.Name.Contains('\r') || child.Name.Contains('\n'))
                    throw new IOException("Invalid name in directory listing.");
                var childPath = Validate(folder + "/" + child.Name);
                if (child.IsDirectory && !child.IsSymbolicLink) pending.Push(childPath);
                else await Checked($"DELE {childPath}");
            }
        }
        while (directories.Count > 0)
        {
            var folder = directories.Pop();
            // LIST may change CWD. Many servers refuse RMD on the current
            // directory; leave it before removing it, deepest folders first.
            if (ftp)
            {
                var parent = folder[..folder.LastIndexOf('/')];
                await Checked($"CWD {(parent.Length == 0 ? "/" : parent)}");
            }
            await Checked($"RMD {folder}");
        }
        await Verify();
    }

    private static string Validate(string path)
    {
        path = path.Replace('\\', '/').TrimEnd('/');
        if (!path.StartsWith('/') || path.Length == 0 || path.Contains('\r') || path.Contains('\n') ||
            path.Split('/').Any(segment => segment is "." or ".."))
            throw new IOException("Refusing to delete an invalid path or the server root.");
        return path;
    }
}
