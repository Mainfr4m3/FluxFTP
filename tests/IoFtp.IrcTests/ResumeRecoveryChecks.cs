using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;
internal static class ResumeRecoveryChecks
{
    public static async Task Run()
    {
        var offsets = new List<long>(); var resets = 0; var logs = new List<string>();
        await ResumeRecovery.RunAsync(50, true, offset => { offsets.Add(offset); return offset > 0 ? Task.FromException(new FtpResumeRejectedException("REST unsupported", 500)) : Task.CompletedTask; }, () => { resets++; return Task.CompletedTask; }, logs.Add, CancellationToken.None);
        if (!offsets.SequenceEqual(new long[] { 50, 0 }) || resets != 1 || logs.Count != 1) throw new Exception("Expected one reset and one overwrite");
        offsets.Clear(); resets = 0;
        await ResumeRecovery.RunAsync(50, true, offset => { offsets.Add(offset); return offset > 0 ? Task.FromException(new FtpResumeRejectedException("553 Error: file permissions incorrect for resuming/appending (not 644).", 553)) : Task.CompletedTask; }, () => { resets++; return Task.CompletedTask; }, null, CancellationToken.None);
        if (!offsets.SequenceEqual(new long[] { 50, 0 }) || resets != 1) throw new Exception("Append permission rejection must retry with overwrite once");
        if (new FtpResumeRejectedException("553 Permission denied", 553).CanOverwrite) throw new Exception("Generic permission denial must not retry");
        foreach (var error in new Exception[] { new FtpResumeRejectedException("REST failed", 500), new FtpResumeRejectedException("dupe", 553), new FtpResumeRejectedException("closed", 421), new FtpResumeRejectedException("File is being uploaded", 550), new OperationCanceledException(), new TimeoutException(), new IOException("network") })
        {
            var enabled = error is not FtpResumeRejectedException { StatusCode: 500 };
            resets = 0;
            try { await ResumeRecovery.RunAsync(50, enabled, _ => Task.FromException(error), () => { resets++; return Task.CompletedTask; }, null, CancellationToken.None); throw new Exception("Error swallowed"); }
            catch (Exception actual) when (ReferenceEquals(actual, error)) { }
            if (resets != 0) throw new Exception("Unsafe overwrite fallback");
        }
        var attempts = 0; resets = 0;
        try { await ResumeRecovery.RunAsync(50, true, _ => { attempts++; return Task.FromException(new FtpResumeRejectedException("failed", 500)); }, () => { resets++; return Task.CompletedTask; }, null, CancellationToken.None); throw new Exception("Second rejection swallowed"); }
        catch (FtpResumeRejectedException) { }
        if (attempts != 2 || resets != 1) throw new Exception("Unbounded fallback");
        using var stop = new CancellationTokenSource(); resets = 0;
        try { await ResumeRecovery.RunAsync(50, true, _ => { stop.Cancel(); return Task.FromException(new FtpResumeRejectedException("failed", 500)); }, () => { resets++; return Task.CompletedTask; }, null, stop.Token); throw new Exception("Cancelled rejection swallowed"); }
        catch (FtpResumeRejectedException) { }
        if (resets != 0) throw new Exception("Cancellation triggered overwrite");
        var folder = Path.Combine(Path.GetTempPath(), "FluxFTP-resume-recovery-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "file.bin"); File.WriteAllText(file + ".FLUXFTP.PART", "old partial content");
            resets = 0;
            var result = await LocalDownload.RunAsync(file, async (stream, offset) => { if (offset > 0) throw new FtpResumeRejectedException("REST unsupported", 502); await stream.WriteAsync(new byte[] { 1, 2, 3 }); }, null, CancellationToken.None, overwriteIfResumeRejected: true, reconnect: () => { resets++; return Task.CompletedTask; });
            if (resets != 1 || result.ResumeOffset != 0 || result.Bytes != 3 || !File.ReadAllBytes(file).SequenceEqual(new byte[] { 1, 2, 3 })) throw new Exception("Download overwrite contents/statistics");
        }
        finally { Directory.Delete(folder, true); }
        GlobalSettingsChecks.Run();
        Console.WriteLine("PASS: opt-in resume recovery, single overwrite, cancellation/timeout/dupe exclusions, download truncation and byte accounting, settings persistence.");
    }
}
