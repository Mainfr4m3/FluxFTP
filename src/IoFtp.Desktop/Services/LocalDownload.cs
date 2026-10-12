using System.IO;
using System.Diagnostics;

namespace IoFtp.Desktop.Services;

internal static class LocalDownload
{
    internal sealed record Measurement(long Bytes, long ResumeOffset, double ElapsedSeconds, double FlushSeconds, double FinalizeSeconds);
    // Async reads may complete synchronously; keep the entire data loop and
    // filesystem work off the WPF dispatcher, including SFTP's blocking calls.
    internal static Task<Measurement> RunAsync(string destination, Func<Stream, long, Task> download,
        IProgress<long>? progress, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var elapsed = Stopwatch.StartNew();
        long resume, bytes;
        double flush;
        var partial = destination + ".FLUXFTP.PART";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var output = new FileStream(partial, FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 64 * 1024, true))
        {
            output.Seek(0, SeekOrigin.End);
            resume = output.Length;
            progress?.Report(output.Length);
            await download(output, output.Length).ConfigureAwait(false);
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
}
