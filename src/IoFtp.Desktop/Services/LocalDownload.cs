using System.IO;
using System.Diagnostics;

namespace IoFtp.Desktop.Services;

internal static class LocalDownload
{
    internal sealed record Measurement(long Bytes, long ResumeOffset, double ElapsedSeconds, double FlushSeconds, double FinalizeSeconds);
    internal sealed record SaveProgress(long Bytes, long Total);
    // Async reads may complete synchronously; keep the entire data loop and
    // filesystem work off the WPF dispatcher, including SFTP's blocking calls.
    internal static Task<Measurement> RunAsync(string destination, Func<Stream, long, Task> download,
        IProgress<long>? progress, CancellationToken cancellationToken, bool allowResume = true, bool overwriteIfResumeRejected = false, Func<Task>? reconnect = null, Action<string>? log = null, string? tempDirectory = null, string sourceIdentity = "", IProgress<SaveProgress>? savingProgress = null) => Task.Run(async () =>
    {
        if (!string.IsNullOrWhiteSpace(tempDirectory))
            return await RunStagedAsync(destination, download, progress, cancellationToken, allowResume,
                overwriteIfResumeRejected, reconnect, log, tempDirectory, sourceIdentity, savingProgress).ConfigureAwait(false);
        var elapsed = Stopwatch.StartNew();
        long resume, bytes;
        double flush;
        var partial = destination + ".FLUXFTP.PART";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var output = new FileStream(partial, FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 64 * 1024, true))
        {
            if (!allowResume) output.SetLength(0);
            output.Seek(0, SeekOrigin.End);
            resume = output.Length;
            progress?.Report(output.Length);
            await ResumeRecovery.RunAsync(resume, overwriteIfResumeRejected, offset => download(output, offset), async () =>
            {
                if (reconnect is not null) await reconnect().ConfigureAwait(false);
                output.SetLength(0); output.Position = 0; resume = 0; progress?.Report(0);
            }, log, cancellationToken).ConfigureAwait(false);
            bytes = output.Length;
            var flushing = Stopwatch.StartNew();
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            flush = flushing.Elapsed.TotalSeconds;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var finalizing = Stopwatch.StartNew();
        File.Move(partial, destination, true);
        return new Measurement(bytes - resume, resume, elapsed.Elapsed.TotalSeconds, flush, finalizing.Elapsed.TotalSeconds);
    }, cancellationToken);
    private static async Task<Measurement> RunStagedAsync(string destination, Func<Stream, long, Task> download,
        IProgress<long>? progress, CancellationToken token, bool allowResume, bool overwriteIfResumeRejected,
        Func<Task>? reconnect, Action<string>? log, string tempDirectory, string sourceIdentity, IProgress<SaveProgress>? savingProgress)
    {
        var folder = Environment.ExpandEnvironmentVariables(tempDirectory.Trim());
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Download temp folder must be an absolute path.");
        folder = Path.GetFullPath(folder);
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(folder);
        // Destination and source identity isolate same-named files from different sites/folders.
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(destination.ToUpperInvariant() + "\n" + sourceIdentity)));
        var ready = Path.Combine(folder, "FluxFTP-" + key + ".ready");
        Measurement measurement;
        if (File.Exists(ready))
        {
            measurement = new(0, new FileInfo(ready).Length, 0, 0, 0);
            log?.Invoke("Using completed temp file; retrying destination copy.");
        }
        else
            measurement = await RunAsync(ready, download, progress, token, allowResume, overwriteIfResumeRejected, reconnect, log).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        // Copy to a sibling first: never expose an incomplete final file or replace an existing one on failure.
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var pending = destination + ".FLUXFTP.COPY-" + key;
        long copied = 0;
        await using (var input = new FileStream(ready, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
        await using (var output = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            var total = input.Length;
            savingProgress?.Report(new(0, total));
            var lastReport = Stopwatch.GetTimestamp();
            var buffer = new byte[1024 * 1024];
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                copied += count;
                var now = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(lastReport, now).TotalMilliseconds >= 100)
                { savingProgress?.Report(new(copied, total)); lastReport = now; }
            }
            await output.FlushAsync(token).ConfigureAwait(false);
            if (copied != total || output.Length != total) throw new IOException("Destination copy size does not match the completed temp file.");
            savingProgress?.Report(new(copied, total));
        }
        token.ThrowIfCancellationRequested();
        File.Move(pending, destination, true);
        // Failure before the final rename leaves the complete temp file available for retry.
        try { File.Delete(ready); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Do not reuse an obsolete ready file on a later download to the same destination.
            try { File.Move(ready, ready + ".saved-" + Guid.NewGuid().ToString("N")); }
            catch (Exception renameError) when (renameError is IOException or UnauthorizedAccessException)
            { throw new IOException("Destination saved, but temp cleanup failed. Remove the retained temp file before retrying: " + ready, renameError); }
            log?.Invoke("Destination saved; temp cleanup retained a .saved file: " + ex.Message);
        }
        return measurement with { ElapsedSeconds = measurement.ElapsedSeconds + timer.Elapsed.TotalSeconds,
            FinalizeSeconds = measurement.FinalizeSeconds + timer.Elapsed.TotalSeconds };
    }

}
