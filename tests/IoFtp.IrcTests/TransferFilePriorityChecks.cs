using System.Text.Json;
using IoFtp.Core.Models;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;
using IoFtp.Engine.Abstractions;
using IoFtp.Engine.Models;
using IoFtp.Engine.Scheduling;

internal static class TransferFilePriorityChecks
{
    public static async Task Run()
    {
        var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
        var settings = new GlobalSettings();
        int Rank(string name, string path) => TransferFilePriority.Rank(name, path, settings);
        var files = new[] { ("release.nfo", "/release/release.nfo"), ("image.jpg", "/release/Proof/image.jpg"),
            ("clip.mkv", "/release/Sample/clip.mkv"), ("release.r00", "/release/release.r00"), ("release.sfv", "/release/release.sfv") };
        Check(files.OrderBy(file => Rank(file.Item1, file.Item2)).Select(file => file.Item1)
            .SequenceEqual(new[] { "release.sfv", "release.r00", "clip.mkv", "image.jpg", "release.nfo" }), "default category order");
        Check(TransferFilePriority.Category("clip.mkv", @"C:\release\sAmPlE\nested\clip.mkv") == "Sample", "Windows and nested Sample paths");
        Check(TransferFilePriority.Category("release.sfv", "/release/PROOF/release.sfv") == "Proof", "SFV in Proof stays with Proof");
        Check(TransferFilePriority.Category("release.nfo", "/release/Sample/release.nfo") == "Sample", "NFO in Sample stays with Sample");
        Check(TransferFilePriority.Category("sample.mkv", "/release/sample.mkv") == "Main files", "filename alone does not imply Sample folder");
        Check(TransferFilePriority.Category("image.jpg", "/release/Proofreader/image.jpg") == "Main files", "folder names match whole segments");
        Check(TransferFilePriority.Category("disc.rar", "/release/CD1/disc.rar") == "Main files", "main files include disc subfolders");
        var profile = new ConnectionProfile(Guid.NewGuid(), "test", "example.invalid", 21, "test", TransferProtocol.Ftp,
            Options: new SiteOptions(ImportedPriorityRules: "1*.nfo\n1*.r01"));
        int Imported(string name) => TransferFilePriority.Rank(name, "/release/" + name, settings, [profile]);
        Check(Imported("release.sfv") < Imported("release.nfo"), "imported NFO priority cannot cross categories");
        Check(Imported("release.r01") < Imported("release.r00"), "imported wildcard applies within main files");
        var reordered = settings with { TransferCategoryOrder = "NFO\nSFV\nMain files\nProof\nSample" };
        Check(TransferFilePriority.Rank("release.nfo", "/release/release.nfo", reordered) <
            TransferFilePriority.Rank("release.sfv", "/release/release.sfv", reordered), "custom category order");
        Check(JsonSerializer.Deserialize<GlobalSettings>("{}")!.TransferCategoryOrder == TransferFilePriority.DefaultOrder, "existing settings gain default categories");
        Check(JsonSerializer.Deserialize<GlobalSettings>(JsonSerializer.Serialize(reordered))!.TransferCategoryOrder == reordered.TransferCategoryOrder, "category order persists");
        Check(TransferFilePriority.Categories("SFV\nSFV").SequenceEqual(TransferFilePriority.Categories(null)), "invalid categories fall back to complete defaults");

        var executor = new Recorder();
        await using var engine = new GlobalTransferEngine(executor);
        var source = Guid.NewGuid(); var target = Guid.NewGuid();
        engine.RegisterOrUpdateSite(new(source, "source", 1, 1, 1));
        engine.RegisterOrUpdateSite(new(target, "target", 1, 1, 1));
        var running = new TransferWorkItem(Guid.NewGuid(), Guid.NewGuid(), "running", source, target, "/running", "/running", 1);
        engine.Enqueue([running]);
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var work = files.Select(file => new TransferWorkItem(Guid.NewGuid(), Guid.NewGuid(), file.Item1, source, target,
            file.Item2, "/target/" + file.Item1, 1, QueuedAt: DateTimeOffset.UtcNow, FilePriorityRank: Rank(file.Item1, file.Item2))).ToArray();
        engine.Enqueue(work);
        engine.UpdateFilePriorities(work.ToDictionary(item => item.Id, item => TransferFilePriority.Rank(item.Name, item.SourcePath, reordered)));
        Check(engine.Snapshot().Single(status => status.Item.Id == running.Id).State == TransferWorkState.Running, "reprioritization leaves running transfer intact");
        executor.Release.TrySetResult();
        await executor.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(executor.Names.SequenceEqual(new[] { "running", "release.nfo", "release.sfv", "release.r00", "image.jpg", "clip.mkv" }), "scheduler follows changed order for pending FXP files");
        Console.WriteLine($"PASS: {count} transfer category, imported priority, persistence and scheduler checks.");
    }

    private sealed class Recorder : ITransferExecutor
    {
        public List<string> Names { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ExecuteAsync(TransferWorkItem item, CancellationToken cancellationToken)
        {
            Names.Add(item.Name);
            if (item.Name == "running") { Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            if (Names.Count == 6) Finished.TrySetResult();
        }
    }
}
