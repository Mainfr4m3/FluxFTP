using System.Net.Sockets;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;
using IoFtp.Engine.Abstractions;
using IoFtp.Engine.Models;
using IoFtp.Engine.Scheduling;
internal static class SiteHealthChecks
{
    public static async Task Run()
    {
        var count = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("Site health: " + label); count++; }
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var profile = new ConnectionProfile(Guid.NewGuid(), "TEST", "127.0.0.1", 21, "!exact-user", TransferProtocol.FtpsExplicit);
        var calls = 0; var result = SiteProbeResult.Unreachable;
        var allowed = new Dictionary<Guid, bool>();
        await using var monitor = new SiteHealthMonitor((_, _) => { calls++; return Task.FromResult(result); }, (id, available) => allowed[id] = available, _ => { }, () => now);
        await monitor.CheckAsync([profile], default);
        Check(calls == 0, "disabled monitor made a connection");
        var policy = new SiteHealthPolicy(true);
        monitor.Configure(policy);
        await monitor.CheckAsync([profile], default);
        Check(calls == 1 && monitor.IsAvailable(profile.Id) && monitor.Snapshot(profile.Id)?.Failures == 1, "first failure paused too early");
        monitor.ObserveFailure(profile, new SocketException((int)SocketError.ConnectionRefused));
        Check(monitor.Snapshot(profile.Id)?.Failures == 1, "same connection burst counted as separate failures");
        now = now.AddSeconds(59); await monitor.CheckAsync([profile], default);
        Check(calls == 1, "retry ran before configured interval");
        now = now.AddSeconds(1); await monitor.CheckAsync([profile], default);
        Check(calls == 2 && !monitor.IsAvailable(profile.Id) && allowed[profile.Id] == false && monitor.Snapshot(profile.Id)?.State == SiteHealthState.Offline, "repeated failure did not suspend trade");
        result = SiteProbeResult.Busy; now = now.AddSeconds(60); await monitor.CheckAsync([profile], default);
        Check(!monitor.IsAvailable(profile.Id), "full server resumed trade before successful authentication");
        result = SiteProbeResult.Online; now = now.AddSeconds(60); await monitor.CheckAsync([profile], default);
        Check(monitor.IsAvailable(profile.Id) && allowed[profile.Id] && monitor.Snapshot(profile.Id)?.Failures == 0, "successful probe did not restore trade");
        var before = calls; now = now.AddSeconds(299); await monitor.CheckAsync([profile], default);
        Check(calls == before, "healthy check ran early");
        now = now.AddSeconds(1); await monitor.CheckAsync([profile], default);
        Check(calls == before + 1, "healthy check interval not applied");
        monitor.ObserveFailure(profile, new FtpCommandException(530, "Access denied", profile.Name, "USER"));
        Check(!monitor.IsAvailable(profile.Id) && monitor.Snapshot(profile.Id)?.State == SiteHealthState.LoginFailed, "login rejection must pause separately");
        before = calls; now = now.AddSeconds(60); await monitor.CheckAsync([profile], default);
        Check(calls == before, "blocked account was retried too quickly");
        monitor.Configure(policy with { CheckSeconds = 600 });
        Check(!monitor.IsAvailable(profile.Id), "changing interval prematurely resumed a paused site");
        now = now.AddSeconds(540); await monitor.CheckAsync([profile], default);
        Check(monitor.IsAvailable(profile.Id), "account recovery did not restore trade");
        foreach (var error in new Exception[] { new FtpCommandException(550, "File locked", profile.Name, "STOR"), new FtpCommandException(553, "Already uploaded", profile.Name, "STOR"),
            new FtpCommandException(530, "Access denied", profile.Name, "LIST"), new OperationCanceledException(), new IOException("wrapper", new FtpCommandException(550, "No such file")) })
            Check(SiteHealthMonitor.Classify(error) is null, "file error/cancellation incorrectly marks site offline");
        Check(SiteHealthMonitor.Classify(new FtpCommandException(421, "Too many connections", profile.Name, "USER")) == SiteProbeResult.Busy, "421 login limit misclassified");
        Check(SiteHealthMonitor.Classify(new FtpCommandException(530, "Maximum users reached", profile.Name, "USER")) == SiteProbeResult.Busy, "530 login limit misclassified");
        Check(SiteHealthMonitor.Classify(new IOException("socket wrapper", new SocketException((int)SocketError.ConnectionRefused))) == SiteProbeResult.Unreachable, "socket outage not detected");
        now = now.AddSeconds(2); monitor.ObserveFailure(profile, new SocketException((int)SocketError.ConnectionRefused));
        monitor.ObserveFailure(profile, new FtpCommandException(421, "Too many connections"));
        Check(monitor.Snapshot(profile.Id)?.Failures == 0, "responsive busy server did not break consecutive failures");
        monitor.ObserveFailure(profile, new FtpCommandException(530, "Blocked", profile.Name, "PASS"));
        monitor.Configure(policy with { Enabled = false });
        Check(monitor.IsAvailable(profile.Id) && allowed[profile.Id], "disabling monitor left health suspension behind");
        monitor.Configure(policy); result = SiteProbeResult.Unreachable;
        now = now.AddSeconds(60); await monitor.CheckAsync([profile], default);
        now = now.AddSeconds(60); await monitor.CheckAsync([profile], default);
        Check(!monitor.IsAvailable(profile.Id), "second outage failed to suspend");
        var edited = profile with { Host = "changed.invalid" };
        result = SiteProbeResult.Deferred; await monitor.CheckAsync([edited], default);
        Check(!monitor.IsAvailable(profile.Id), "editing offline site enabled trade before verification");
        monitor.ObserveSuccess(profile);
        Check(!monitor.IsAvailable(profile.Id), "old login activity enabled an edited site before verification");
        await monitor.CheckAsync([], default);
        Check(monitor.IsAvailable(profile.Id) && allowed[profile.Id], "removed site retained a health suspension");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<SiteProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var racing = new SiteHealthMonitor(async (_, _) => { started.TrySetResult(); return await finish.Task; }, (_, _) => { }, _ => { }, () => now);
        racing.Configure(policy with { Failures = 1 });
        var pending = racing.CheckAsync([profile], default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        racing.ObserveSuccess(profile); finish.SetResult(SiteProbeResult.Unreachable); await pending;
        Check(racing.IsAvailable(profile.Id) && racing.Snapshot(profile.Id)?.State == SiteHealthState.Online, "stale probe failure overrode newer successful activity");
        var active = 0; var peak = 0;
        await using var bounded = new SiteHealthMonitor(async (_, token) => { var n = Interlocked.Increment(ref active); peak = Math.Max(peak, n); try { await Task.Delay(20, token); return SiteProbeResult.Online; } finally { Interlocked.Decrement(ref active); } }, (_, _) => { }, _ => { });
        bounded.Configure(policy);
        await bounded.CheckAsync(Enumerable.Range(0, 8).Select(i => profile with { Id = Guid.NewGuid(), Name = "site" + i }), default);
        Check(peak == 2 && active == 0, "probe concurrency exceeds two or leaked an active probe");
        var executor = new BlockingExecutor();
        await using var engine = new GlobalTransferEngine(executor);
        var target = Guid.NewGuid(); var other = Guid.NewGuid();
        engine.RegisterOrUpdateSite(new(profile.Id, "source", 1, 1, 1)); engine.RegisterOrUpdateSite(new(target, "target", 1, 1, 1)); engine.RegisterOrUpdateSite(new(other, "other", 1, 1, 1));
        var first = Guid.NewGuid(); var queued = Guid.NewGuid(); var paused = Guid.NewGuid(); var unrelated = Guid.NewGuid();
        TransferWorkItem Job(Guid id, Guid source) => new(id, id, id.ToString(), source, target, "/a", "/b", 1);
        engine.Enqueue([Job(first, profile.Id)]);
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        engine.SetSiteAvailable(profile.Id, false);
        engine.Enqueue([Job(queued, profile.Id), Job(paused, profile.Id)]); engine.Pause(paused);
        engine.RegisterOrUpdateSite(new(profile.Id, "source updated", 2, 2, 2));
        Check(engine.Snapshot().Single(job => job.Item.Id == first).State == TransferWorkState.Running, "health suspension cancelled active transfer");
        executor.Release.SetResult();
        await Until(() => engine.Snapshot().Single(job => job.Item.Id == first).State == TransferWorkState.Completed);
        Check(engine.Snapshot().Single(job => job.Item.Id == queued).State == TransferWorkState.Queued, "policy refresh bypassed health suspension");
        engine.Enqueue([Job(unrelated, other)]);
        await Until(() => engine.Snapshot().Single(job => job.Item.Id == unrelated).State == TransferWorkState.Completed);
        Check(engine.Snapshot().Single(job => job.Item.Id == queued).State == TransferWorkState.Queued, "offline route ran while other route was active");
        engine.SetSiteAvailable(profile.Id, true);
        await Until(() => engine.Snapshot().Single(job => job.Item.Id == queued).State == TransferWorkState.Completed);
        Check(engine.Snapshot().Single(job => job.Item.Id == paused).State == TransferWorkState.Paused, "automatic resume changed manual pause");
        engine.DisconnectSite(profile.Id); engine.SetSiteAvailable(profile.Id, false); engine.SetSiteAvailable(profile.Id, true);
        var disconnected = Guid.NewGuid(); engine.Enqueue([Job(disconnected, profile.Id)]); await Task.Delay(60);
        Check(engine.Snapshot().Single(job => job.Item.Id == disconnected).State == TransferWorkState.Queued, "health recovery reenabled explicitly disconnected site");
        Console.WriteLine($"PASS: {count} site health checks: outage/recovery, timing, login backoff, busy/file exclusions, settings changes, stale probes, bounded concurrency, queued work, active transfers and manual pauses.");
    }
    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Engine did not reach expected state"); await Task.Delay(10); }
    }
    private sealed class BlockingExecutor : ITransferExecutor
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ExecuteAsync(TransferWorkItem item, CancellationToken token) { Started.TrySetResult(); await Release.Task.WaitAsync(token); }
    }
}
