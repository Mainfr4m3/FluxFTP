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
            Task<LocalDownload.Measurement> transfer;
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
            var measurement = await transfer.WaitAsync(TimeSpan.FromSeconds(10));
            if (measurement.Bytes != 1 || measurement.ResumeOffset != 2 || measurement.ElapsedSeconds < 0 ||
                measurement.FlushSeconds < 0 || measurement.FinalizeSeconds < 0)
                throw new Exception("Download measurement must count new bytes separately from resumed bytes.");
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
            await StagedChecks(directory);
            Console.WriteLine("PASS: background download, partial resume, finalization and cancellation.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class Sink<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
    private static async Task StagedChecks(string directory)
    {
        var staging = Path.Combine(directory, "ram");
        var destination = Path.Combine(directory, "network", "file.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, [9]);
        var payload = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();
        var networkCalls = 0;
        async Task Download(Stream stream, long offset)
        {
            networkCalls++;
            if (offset != 0) throw new Exception("Unexpected initial temp offset");
            if (!(await File.ReadAllBytesAsync(destination)).SequenceEqual(new byte[] { 9 }))
                throw new Exception("Destination touched during network download");
            await stream.WriteAsync(payload);
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var saving = new Sink<LocalDownload.SaveProgress>(sample => { if (sample.Bytes == 0) cancellation.Cancel(); });
            try
            {
                await LocalDownload.RunAsync(destination, Download, null, cancellation.Token,
                    tempDirectory: staging, sourceIdentity: "site:/file", savingProgress: saving);
                throw new Exception("Cancelled saving unexpectedly succeeded");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        if (!(await File.ReadAllBytesAsync(destination)).SequenceEqual(new byte[] { 9 }) || Directory.GetFiles(staging, "*.ready").Length != 1)
            throw new Exception("Cancelled copy replaced destination or lost completed temp file");
        var reports = new List<LocalDownload.SaveProgress>();
        var result = await LocalDownload.RunAsync(destination, (_, _) => throw new Exception("Completed temp must not be downloaded again"), null, CancellationToken.None,
            tempDirectory: staging, sourceIdentity: "site:/file", savingProgress: new Sink<LocalDownload.SaveProgress>(reports.Add));
        if (networkCalls != 1 || result.Bytes != 0 || result.ResumeOffset != payload.Length || !File.ReadAllBytes(destination).SequenceEqual(payload)
            || Directory.GetFiles(staging).Length != 0 || reports.First().Bytes != 0 || reports.Last().Bytes != payload.Length)
            throw new Exception("Temp reuse/copy progress/final cleanup failed");
        // Different source identity must not reuse another site's partial download.
        var interrupted = Path.Combine(directory, "network", "resume.bin");
        using (var stop = new CancellationTokenSource())
        {
            try
            {
                await LocalDownload.RunAsync(interrupted, async (stream, offset) =>
                {
                    await stream.WriteAsync(new byte[] { 1, 2 }); stop.Cancel(); stop.Token.ThrowIfCancellationRequested();
                }, null, stop.Token, tempDirectory: staging, sourceIdentity: "A");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        var isolated = Path.Combine(directory, "network", "isolated.bin");
        await LocalDownload.RunAsync(isolated, async (stream, offset) =>
        { if (offset != 0) throw new Exception("Temp paths collided"); await stream.WriteAsync(new byte[] { 7 }); },
            null, CancellationToken.None, tempDirectory: staging, sourceIdentity: "B");
        await LocalDownload.RunAsync(interrupted, async (stream, offset) =>
        { if (offset != 2) throw new Exception("Temp partial not resumed"); await stream.WriteAsync(new byte[] { 3 }); },
            null, CancellationToken.None, tempDirectory: staging, sourceIdentity: "A");
        if (!File.ReadAllBytes(interrupted).SequenceEqual(new byte[] { 1, 2, 3 })) throw new Exception("Staged resume corrupt");
        // A destination failure retains the ready file for retry without contacting FTP.
        var blocked = Path.Combine(directory, "network", "blocked"); Directory.CreateDirectory(blocked);
        try
        {
            await LocalDownload.RunAsync(blocked, async (stream, offset) => await stream.WriteAsync(new byte[] { 4, 5 }),
                null, CancellationToken.None, tempDirectory: staging, sourceIdentity: "blocked");
            throw new Exception("Invalid destination succeeded");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (Directory.GetFiles(staging, "*.ready").Length != 1) throw new Exception("Copy failure lost temp file");
        Directory.Delete(blocked);
        await LocalDownload.RunAsync(blocked, (_, _) => throw new Exception("Copy failure caused redundant FTP download"),
            null, CancellationToken.None, tempDirectory: staging, sourceIdentity: "blocked");
        if (!File.ReadAllBytes(blocked).SequenceEqual(new byte[] { 4, 5 }) || Directory.GetFiles(staging).Length != 0)
            throw new Exception("Copy retry failed");
        Console.WriteLine("PASS: staged download, untouched final file, copy cancellation, ready-file retry, isolated temp paths, partial resume and copy progress.");
    }


}
