using System.Collections.Concurrent;
using IoFtp.Engine.Abstractions;
using IoFtp.Engine.Models;
using IoFtp.Engine.Scheduling;

internal static class HardSlotLimitChecks
{
    public static async Task Run()
    {
        var site = Guid.NewGuid();
        var other = Guid.NewGuid();
        var executor = new Executor();
        await using var engine = new GlobalTransferEngine(executor);
        engine.RegisterOrUpdateSite(new(site, "VM", 6, 3, 3));
        engine.RegisterOrUpdateSite(new(other, "Other", 50, 50, 50));
        var oversized = new SitePolicy(site, "VM", 100, 100, 100);
        var jobs = Enumerable.Range(0, 12).Select(i => new TransferWorkItem(Guid.NewGuid(), Guid.NewGuid(), $"file{i}",
            i % 2 == 0 ? site : other, i % 2 == 0 ? other : site, "/source", "/target", 1,
            SourceLimits: i % 2 == 0 ? oversized : null, DestinationLimits: i % 2 == 0 ? null : oversized)).ToArray();
        engine.Enqueue(jobs);
        await Wait(() => engine.Snapshot().Count(job => job.State == TransferWorkState.Running) == 6 && executor.Started.Count == 6);
        var active = engine.Snapshot().Where(job => job.State == TransferWorkState.Running).ToArray();
        if (active.Count(job => job.Item.SourceSiteId == site) != 3 || active.Count(job => job.Item.DestinationSiteId == site) != 3)
            throw new Exception("Per-job limits bypassed the live 3 upload / 3 download policy.");
        engine.RegisterOrUpdateSite(new(site, "VM", 4, 3, 3));
        if (active.Any(job => executor.Tokens[job.Item.Id].IsCancellationRequested))
            throw new Exception("Lowering the total cancelled active transfers.");
        var down = active.Where(job => job.Item.SourceSiteId == site).ToArray();
        foreach (var job in down.Take(2)) executor.Started[job.Item.Id].TrySetResult();
        await Wait(() => engine.Snapshot().Count(job => job.State == TransferWorkState.Completed) == 2);
        if (executor.Started.Count != 6) throw new Exception("Lowered total admitted work before active slots drained below 4.");
        executor.Started[down[2].Item.Id].TrySetResult();
        await Wait(() => executor.Started.Count == 7);
        if (engine.Snapshot().Count(job => job.State == TransferWorkState.Running) > 4)
            throw new Exception("Current total limit was exceeded.");
        executor.ReleaseAll = true;
        foreach (var lease in executor.Started.Values) lease.TrySetResult();
        await Wait(() => engine.Snapshot().All(job => job.State == TransferWorkState.Completed));
        Console.WriteLine("PASS: shared 3/3 directional limits, hard total across jobs, oversized manual limits and lowering total without cancelling active jobs.");
    }
    private static async Task Wait(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready()) await Task.Delay(10, timeout.Token);
    }
    private sealed class Executor : ITransferExecutor
    {
        public readonly ConcurrentDictionary<Guid, TaskCompletionSource> Started = new();
        public readonly ConcurrentDictionary<Guid, CancellationToken> Tokens = new();
        public volatile bool ReleaseAll;
        public async Task ExecuteAsync(TransferWorkItem item, CancellationToken token)
        {
            Tokens[item.Id] = token;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Started[item.Id] = release;
            if (ReleaseAll) release.TrySetResult();
            await release.Task.WaitAsync(token);
        }
    }
}
