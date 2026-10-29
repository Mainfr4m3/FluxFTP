using System.Collections.Concurrent;
using IoFtp.Core.Abstractions;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;

internal static class LockedDirectoryChecks
{
    public static async Task Run()
    {
        var sourceError = new FtpCommandException(550, "550 Permission denied", "GBIT01", "CWD /release");
        if (LockedDirectorySkip.Match(new IOException("listing failed", sourceError), "GBIT01", "TIS") != new LockedDirectorySkip("GBIT01", false))
            throw new Exception("Wrapped source directory lock was not classified.");
        var targetError = new DestinationDirectoryException("/release", new(550, "550 Permission denied"), new(550, "550 File exists"), "GBIT02");
        if (LockedDirectorySkip.Match(targetError, "CR", "GBIT02") != new LockedDirectorySkip("GBIT02", true))
            throw new Exception("Destination directory lock was not classified.");
        foreach (var error in new Exception[] {
            new FtpCommandException(550, "550 Permission denied", "GBIT02", "STOR file.rar"),
            new FtpCommandException(550, "550 No such file or directory", "GBIT02", "CWD /release"),
            new FtpCommandException(530, "530 Access denied", "GBIT02", "CWD /release"),
            new FtpCommandException(550, "550 Permission denied", "TIS", "CWD /release"),
            new FtpCommandException(550, "550 File locking failed", "GBIT02", "STOR file.rar"),
            new IOException("connection lost") })
            if (LockedDirectorySkip.Match(error, "CR", "GBIT02") is not null) throw new Exception("Unrelated file/login/network failure skipped the whole directory.");
        var a = new VisionaryRaceState(new());
        var b = new VisionaryRaceState(new()) { DestinationOwner = a.DestinationOwner };
        var other = new VisionaryRaceState(new());
        a.SkipDirectory(new("GBIT02", true));
        if (b.DirectorySkip?.Site != "GBIT02" || other.DirectorySkip is not null)
            throw new Exception("Destination skip must be shared only by sources for that destination race.");
        try { b.ThrowIfStopped(); throw new Exception("Skipped directory accepted more work."); }
        catch (RaceDirectorySkippedException) { }
        var source = new VisionaryRaceState(new());
        var healthy = new VisionaryRaceState(new()) { DestinationOwner = source.DestinationOwner };
        source.SkipDirectory(new("GBIT01", false));
        if (healthy.DirectorySkip is not null) throw new Exception("A source skip stopped a healthy source.");

        var logs = new ConcurrentQueue<string>();
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var denied = 0;
        await using (var receiver = new VisionaryPipeServer(_ => Task.FromResult<object>(new { ok = true }), log: text => {
            logs.Enqueue(text); if (text.Contains("-> TIS COMPLETE")) complete.TrySetResult();
        }, raceStep: (request, _, token) => {
            token.ThrowIfCancellationRequested();
            if (request.DstSite == "GBIT02") { Interlocked.Increment(ref denied); throw targetError; }
            return Task.FromResult(true);
        }))
        {
            await receiver.SubmitChainsAsync("TV", "Locked.Target-GROUP", "CR;GBIT02,C;GBIT02,CR;TIS");
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!logs.Any(line => line.Contains("SKIP [GBIT02]"))) await Task.Delay(10, timeout.Token);
            if (denied != 1 || logs.Any(line => line.Contains("-> GBIT02 COMPLETE") || line.Contains("source FAILED")))
                throw new Exception("Locked destination retried or was reported as failure/success.");
        }
        var alternateComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceCalls = 0;
        var alternateCalls = 0;
        await using (var receiver = new VisionaryPipeServer(_ => Task.FromResult<object>(new { ok = true }), log: text => {
            if (text.Contains("-> TIS COMPLETE")) alternateComplete.TrySetResult();
        }, raceStep: (request, _, token) => {
            token.ThrowIfCancellationRequested();
            if (request.SrcSite == "GBIT01") { Interlocked.Increment(ref sourceCalls); throw sourceError; }
            return Task.FromResult(Interlocked.Increment(ref alternateCalls) >= 2);
        }, sourcePriority: site => site == "GBIT01" ? 100 : 90))
        {
            await receiver.SubmitChainsAsync("TV", "Locked.Source-GROUP", "GBIT01;TIS,CR;TIS");
            await alternateComplete.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (sourceCalls != 1 || alternateCalls < 2) throw new Exception("Locked source repeated or blocked the healthy alternate source.");
        }
        Console.WriteLine("PASS: GBIT directory skip classification, shared destination isolation, source-only skip, no retry/false COMPLETE, and healthy alternative targets/sources.");
    }
}
