using IoFtp.Desktop.Services;

internal static class LocalDownloadChecks
{
    internal static async Task Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-download-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "test.bin");
            var partial = path + ".FLUXFTP.PART";
            await File.WriteAllBytesAsync(partial, [1, 2]);
            var previous = SynchronizationContext.Current;
            var context = new SynchronizationContext();
            Task transfer;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                transfer = LocalDownload.RunAsync(path, (stream, offset) =>
                {
                    if (SynchronizationContext.Current == context)
                        throw new Exception("Download data loop captured caller UI context.");
                    if (offset != 2 || stream.Position != 2)
                        throw new Exception("Download did not resume the partial file.");
                    stream.WriteByte(3);
                    return Task.CompletedTask;
                }, null, CancellationToken.None);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            await transfer.WaitAsync(TimeSpan.FromSeconds(10));
            if (File.Exists(partial) || !(await File.ReadAllBytesAsync(path)).SequenceEqual(new byte[] { 1, 2, 3 }))
                throw new Exception("Completed download was not finalized correctly.");
            using var cancellation = new CancellationTokenSource();
            try
            {
                await LocalDownload.RunAsync(path, (stream, offset) =>
                {
                    stream.WriteByte(4);
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }, null, cancellation.Token);
                throw new Exception("Cancelled download succeeded.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (!File.Exists(partial) || !(await File.ReadAllBytesAsync(path)).SequenceEqual(new byte[] { 1, 2, 3 }))
                throw new Exception("Cancellation replaced the completed file or lost the partial file.");
            Console.WriteLine("PASS: background download, partial resume, finalization and cancellation.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
