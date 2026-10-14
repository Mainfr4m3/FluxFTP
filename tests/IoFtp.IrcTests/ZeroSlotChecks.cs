using IoFtp.Engine.Abstractions;
using IoFtp.Engine.Models;
using IoFtp.Engine.Scheduling;
internal static class ZeroSlotChecks
{
    public static async Task Run()
    {
        var executor = new Recorder();
        await using var engine = new GlobalTransferEngine(executor);
        var source = Guid.NewGuid(); var target = Guid.NewGuid();
        engine.RegisterOrUpdateSite(new(source, "source", 0, 0, 0));
        engine.RegisterOrUpdateSite(new(target, "target", 1, 1, 1));
        engine.Enqueue([new(Guid.NewGuid(), Guid.NewGuid(), "test", source, target, "/a", "/b", 1)]);
        await Task.Delay(100);
        if (executor.Started.Task.IsCompleted || engine.Snapshot().Single().State != TransferWorkState.Queued)
            throw new Exception("Zero slots must wait without running or failing.");
        engine.RegisterOrUpdateSite(new(source, "source", 1, 1, 1));
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { engine.RegisterOrUpdateSite(new(source, "invalid", -1)); throw new Exception("Negative slots accepted"); }
        catch (ArgumentOutOfRangeException) { }
        Console.WriteLine("PASS: zero slots wait, capacity restoration starts FXP, negative slots rejected.");
    }
    private sealed class Recorder : ITransferExecutor
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ExecuteAsync(TransferWorkItem item, CancellationToken token) { Started.TrySetResult(); return Task.CompletedTask; }
    }
}
