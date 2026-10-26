using IoFtp.Core.Transport;
namespace IoFtp.Linux;
internal static class Transfers
{
    public static async Task Run(Job job, IProgress<long> progress, Action<string> log, CancellationToken token, int connectTimeoutSeconds = 30)
    {
        await using var source = new FtpRemoteSession();
        await using var target = new FtpRemoteSession();
        source.ProtocolMessage += log; target.ProtocolMessage += log;
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(connectTimeoutSeconds));
            if (job.Source is not null) await source.ConnectAsync(job.Source, connect.Token);
            if (job.Target is not null) await target.ConnectAsync(job.Target, connect.Token);
        }
        if (job.Source is not null && job.Target is not null)
        {
            if (await target.GetSizeAsync(job.Destination, token) is not null) throw new IOException("Destination exists; preview does not overwrite remote files.");
            await source.FxpToAsync(target, job.File.Path, job.Destination, token); progress.Report(job.File.Size); return;
        }
        if (job.Target is not null)
        {
            if (await target.GetSizeAsync(job.Destination, token) is not null) throw new IOException("Destination exists; preview does not overwrite remote files.");
            await using var input = File.OpenRead(job.File.Path);
            await target.UploadAsync(job.Destination, input, 0, progress, token); return;
        }
        if (File.Exists(job.Destination)) throw new IOException("Destination exists; choose another folder or remove the file first.");
        var partial = job.Destination + ".fluxftp-" + job.Id.ToString("N") + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(job.Destination)!);
        await using (var output = new FileStream(partial, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 65536, true))
        {
            var offset = output.Length;
            if (offset > job.File.Size && job.File.Size > 0) throw new IOException("Partial file is larger than the source.");
            output.Position = offset;
            if (job.Source is not null) await source.DownloadAsync(job.File.Path, output, offset, progress, token);
            else
            {
                await using var input = File.OpenRead(job.File.Path); input.Position = offset;
                var buffer = new byte[65536]; int read; long bytes = offset;
                while ((read = await input.ReadAsync(buffer, token)) > 0) { await output.WriteAsync(buffer.AsMemory(0, read), token); bytes += read; progress.Report(bytes); }
            }
            await output.FlushAsync(token);
            if (job.File.Size > 0 && output.Length != job.File.Size) throw new IOException("Downloaded length does not match the listed size.");
        }
        token.ThrowIfCancellationRequested();
        File.Move(partial, job.Destination, false);
    }
}
