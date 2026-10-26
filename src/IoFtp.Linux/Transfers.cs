using IoFtp.Core.Transport;
namespace IoFtp.Linux;
internal static class Transfers
{
    public static async Task Run(Job job, IProgress<long> progress, Action<string> log, CancellationToken token, int connectTimeoutSeconds = 30)
    {
        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(token);
        connecting.CancelAfter(TimeSpan.FromSeconds(connectTimeoutSeconds));
        using var loginSlots = await LinuxSessionLease.Acquire([job.Source, job.Target], connecting.Token);
        await using var source = new FtpRemoteSession();
        await using var target = new FtpRemoteSession();
        source.ProtocolMessage += log; target.ProtocolMessage += log;
        if (job.Source is not null) await source.ConnectAsync(job.Source, connecting.Token);
        if (job.Target is not null) await target.ConnectAsync(job.Target, connecting.Token);
        connecting.CancelAfter(Timeout.InfiniteTimeSpan);
        if (job.Source is not null && job.Target is not null)
        {
            var existing = await target.GetSizeAsync(job.Destination, token);
            if (!job.ApiTransfer && existing is not null) throw new IOException("Destination exists; preview does not overwrite remote files.");
            if (job.ApiTransfer && existing == job.File.Size) { progress.Report(job.File.Size); return; }
            if (existing > job.File.Size) throw new IOException("Destination is larger than source; refusing to overwrite.");
            await source.FxpToAsync(target, job.File.Path, job.Destination, token, offset: job.ApiTransfer ? existing ?? 0 : 0);
            var completedSize = await target.GetSizeAsync(job.Destination, token);
            if (completedSize != job.File.Size) throw new IOException("FXP target size verification failed; source may still be growing.");
            progress.Report(job.File.Size); return;
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
