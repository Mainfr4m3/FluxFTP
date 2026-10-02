using IoFtp.Engine.Abstractions;
using IoFtp.Engine.Models;
using IoFtp.Engine.Scheduling;
using IoFtp.Desktop.Services;

internal static class LocalTransferChecks
{
    public static async Task Run()
    {
        var executor = new Executor();
        await using var engine = new GlobalTransferEngine(executor);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        engine.RegisterOrUpdateSite(new(a, "A", 6, 3, 3));
        engine.RegisterOrUpdateSite(new(b, "B", 6, 3, 3));
        engine.ConfigureLocalSlots(3, 3);
        TransferWorkItem Item(Guid? source, Guid? target) => new(Guid.NewGuid(), Guid.NewGuid(), "file", source, target, "/a", "/b", 1);
        engine.Enqueue([Item(a, null), Item(null, b), Item(null, null), Item(a, b)]);
        await executor.ParallelFxp.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (executor.LocalActive != 1) throw new Exception("Local transfers must be serial while FXP remains independent.");
        executor.Release.TrySetResult();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (engine.Snapshot().Any(item => item.State != TransferWorkState.Completed))
        {
            if (DateTime.UtcNow > deadline) throw new Exception("Local scheduler did not drain.");
            await Task.Delay(10);
        }
        if (executor.MaxLocal != 1) throw new Exception("Local transfers overlapped.");
        var sink = new Counter(); var progress = new ThrottledTransferProgress(sink);
        for (var i = 0; i < 10000; i++) progress.Report(i);
        if (sink.Count > 10 || sink.Count == 0) throw new Exception("Transfer progress was not throttled before dispatch.");
        Console.WriteLine("PASS: local serial scheduling, independent FXP, throttled progress.");
    }
    private sealed class Counter : IProgress<long> { public int Count; public void Report(long value) => Count++; }
    private sealed class Executor : ITransferExecutor
    {
        public TaskCompletionSource ParallelFxp = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LocalActive, MaxLocal;
        public async Task ExecuteAsync(TransferWorkItem item, CancellationToken token)
        {
            var local = item.SourceSiteId is null || item.DestinationSiteId is null;
            if (local) { var count = Interlocked.Increment(ref LocalActive); MaxLocal = Math.Max(MaxLocal, count); }
            else ParallelFxp.TrySetResult();
            try { await Release.Task.WaitAsync(token); await Task.Delay(10, token); }
            finally { if (local) Interlocked.Decrement(ref LocalActive); }
        }
    }
}
