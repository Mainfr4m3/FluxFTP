using IoFtp.Engine.Abstractions;
using IoFtp.Engine.Models;
using IoFtp.Engine.Scheduling;

internal static class SitePriorityChecks
{
    public static async Task Run()
    {
        var executor = new Recorder(3);
        await using var engine = new GlobalTransferEngine(executor);
        var source = Guid.NewGuid(); var low = Guid.NewGuid(); var high = Guid.NewGuid();
        engine.RegisterOrUpdateSite(new(source, "source", 0, 0, 0));
        engine.RegisterOrUpdateSite(new(low, "low", 1, 1, 1, 1));
        engine.RegisterOrUpdateSite(new(high, "high", 1, 1, 1, 100));
        var now = DateTimeOffset.UtcNow;
        TransferWorkItem Item(string name, Guid src, Guid dst, int rank, int age) =>
            new(Guid.NewGuid(), Guid.NewGuid(), name, src, dst, "/" + name, "/" + name, 1,
                QueuedAt: now.AddMinutes(-age), FilePriorityRank: rank);
        engine.Enqueue([Item("old-low.rar", source, low, 0, 20), Item("new-high.rar", source, high, 0, 0), Item("low.sfv", source, low, -1, 0)]);
        var pending = engine.Snapshot().Select(item => item.Item.Name).ToArray();
        if (!pending.SequenceEqual(new[] { "low.sfv", "new-high.rar", "old-low.rar" })) throw new Exception("Snapshot does not reflect SFV then descending site priority");
        engine.RegisterOrUpdateSite(new(source, "source", 1, 1, 1));
        await executor.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!executor.Names.SequenceEqual(pending)) throw new Exception("New high-priority target must beat old lower-priority target without defeating SFV priority");

        var sources = new Recorder(3);
        await using var second = new GlobalTransferEngine(sources);
        second.RegisterOrUpdateSite(new(low, "low-source", 1, 1, 1, 1));
        second.RegisterOrUpdateSite(new(high, "high-source", 1, 1, 1, 100));
        second.RegisterOrUpdateSite(new(source, "target", 0, 0, 0));
        second.Enqueue([Item("old-low-source.sfv", low, source, -1, 20), Item("new-high-source.rar", high, source, 0, 0), Item("high-source.sfv", high, source, -1, 0)]);
        second.RegisterOrUpdateSite(new(source, "target", 1, 1, 1));
        await sources.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!sources.Names.SequenceEqual(new[] { "high-source.sfv", "new-high-source.rar", "old-low-source.sfv" })) throw new Exception("Higher source priority must precede lower-source SFV; SFV remains first within the selected source");
        Console.WriteLine("PASS: source priority before SFV, SFV within equal source priority, destination priority before age, matching display/execution order.");
    }
    private sealed class Recorder(int count) : ITransferExecutor
    {
        public List<string> Names { get; } = [];
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ExecuteAsync(TransferWorkItem item, CancellationToken token)
        {
            Names.Add(item.Name);
            if (Names.Count == count) Done.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
