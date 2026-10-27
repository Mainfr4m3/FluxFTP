using IoFtp.Core.Models;
using IoFtp.Desktop.Services;
using IoFtp.Engine.Models;
using IoFtp.Engine.Abstractions;
using IoFtp.Engine.Scheduling;

internal static class VisionaryLiveSlotChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var folder = Path.Combine(Path.GetTempPath(), "FluxSlots-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var profile = new ConnectionProfile(Guid.NewGuid(), "JBG", "invalid", 21, "test", TransferProtocol.FtpsExplicit)
                { Options = new SiteOptions(MaxSlots: 1, MaxDownloadSlots: 1, MaxUploadSlots: 1) };
            var cache = new VisionarySlotCache(folder).Load();
            Check(cache.Apply(profile, 2) == profile.EffectiveOptions, "Unknown sites must retain Flux limits");
            cache.Update("JBG", 5, 3);
            var effective = cache.Apply(profile, 2);
            Check(effective.MaxSlots == 1 && RaceSlotBudget.Transfers(effective.MaxSlots, effective.MaxUploadSlots, effective.MaxDownloadSlots, 2) == 0,
                "Visi must never expand the saved total login limit");
            var vm = profile with { Options = new SiteOptions(MaxSlots: 8, MaxDownloadSlots: 3, MaxUploadSlots: 3) };
            cache.Update("JBG", 3, 3);
            foreach (var auxiliary in new[] { 1, 2, 3 })
            {
                var limits = cache.Apply(vm, auxiliary);
                var transfers = RaceSlotBudget.Transfers(limits.MaxSlots, limits.MaxUploadSlots, limits.MaxDownloadSlots, auxiliary);
                Check(limits.MaxSlots == 8 && transfers <= 6 && transfers + auxiliary <= 8,
                    "3 up / 3 down plus shared listing and browsing must fit inside 8 logins");
            }
            cache.Update("JBG", 5, 3);
            cache.Update("jbg", null, null);
            cache = new VisionarySlotCache(folder).Load();
            Check(cache.Get("JBG") == new VisionarySlotCache.Slots(5, 3), "Timeout and restart must preserve last values");
            cache.Update("JBG", 0, null);
            Check(cache.Apply(profile, 1).MaxDownloadSlots == 0 && !cache.Apply(profile, 1).AllowDownload,
                "Explicit Visi zero must disable direction without erasing other direction");
            Check(profile.EffectiveOptions.MaxSlots == 1, "Saved manual limits must not be mutated");
            File.WriteAllText(Path.Combine(folder, "visionary-slots.json"), "broken");
            Check(new VisionarySlotCache(folder).Load().Get("JBG") is null, "Corrupt cache uses Flux fallback");
            var executor = new Executor();
            await using var engine = new GlobalTransferEngine(executor);
            var target = Guid.NewGuid();
            engine.RegisterOrUpdateSite(new(profile.Id, "JBG", 3, 3, 3));
            engine.RegisterOrUpdateSite(new(target, "Target", 3, 3, 3));
            TransferWorkItem Item() => new(Guid.NewGuid(), Guid.NewGuid(), "file", profile.Id, target, "/a", "/b", 1);
            engine.Enqueue([Item(), Item()]);
            await executor.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var manual = Item() with { SourceLimits = new(profile.Id, "JBG", 1, 1, 1) };
            engine.Enqueue([manual, Item()]);
            await executor.ThreeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(engine.Snapshot().Single(x => x.Item.Id == manual.Id).State == TransferWorkState.Queued,
                "Race capacity must not expand manual job limits");
            executor.Release.TrySetResult();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (engine.Snapshot().Any(x => x.State != TransferWorkState.Completed))
            {
                Check(DateTime.UtcNow < deadline, "Manual queue must drain after race slots release");
                await Task.Delay(10);
            }
            Console.WriteLine("PASS: Visi directional limits, hard total including auxiliary sessions, timeout/restart cache, zero, corrupt cache and manual/race isolation.");
        }
        finally { Directory.Delete(folder, true); }
    }
    private sealed class Executor : ITransferExecutor
    {
        private int _started;
        public TaskCompletionSource TwoStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ThreeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ExecuteAsync(TransferWorkItem item, CancellationToken token)
        {
            var count = Interlocked.Increment(ref _started);
            if (count == 2) TwoStarted.TrySetResult();
            if (count == 3) ThreeStarted.TrySetResult();
            await Release.Task.WaitAsync(token);
        }
    }
}
