using System.IO;

namespace IoFtp.Desktop.Services;

internal static class LocalDownload
{
    // Async reads may complete synchronously; keep the entire data loop and
    // filesystem work off the WPF dispatcher, including SFTP's blocking calls.
    internal static Task RunAsync(string destination, Func<Stream, long, Task> download,
        IProgress<long>? progress, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var partial = destination + ".FLUXFTP.PART";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var output = new FileStream(partial, FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 64 * 1024, true))
        {
            output.Seek(0, SeekOrigin.End);
            progress?.Report(output.Length);
            await download(output, output.Length).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(partial, destination, true);
    }, cancellationToken);
}
