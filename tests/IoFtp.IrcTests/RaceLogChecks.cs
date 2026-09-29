using System.IO;
using IoFtp.Desktop.Services;

internal static class RaceLogChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-race-" + Guid.NewGuid());
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("Race log: " + name); count++; }
        try
        {
            var log = new RaceLogStore(directory, () => now);
            log.Write("TEST", "first\r\nline");
            Check(File.ReadAllLines(log.ActivePath).Length == 2, "single line records");
            now = now.AddHours(23).AddMinutes(59);
            log.Maintain();
            Check(!Directory.Exists(log.ArchiveDirectory), "not rotated early");
            log = new RaceLogStore(directory, () => now);
            now = now.AddMinutes(1);
            log.Maintain();
            var archive = Directory.GetFiles(log.ArchiveDirectory).Single();
            Check(File.ReadAllText(archive).Contains("firstline"), "24 hour rotation across restart preserves contents");
            Check(!File.ReadAllText(log.ActivePath).Contains("firstline"), "fresh current log");
            Parallel.For(0, 100, i => log.Write("TRANSFER", "file" + i));
            Check(File.ReadAllLines(log.ActivePath).Length == 101, "concurrent writes retained");
            now = now.AddDays(3);
            log.Maintain();
            Check(Directory.GetFiles(log.ArchiveDirectory).Length == 2, "idle rotation preserves previous archive");
            Check(File.ReadAllText(archive).Contains("firstline"), "old archive unchanged");
            File.AppendAllText(log.ActivePath, new string('x', 600 * 1024) + "\nlast\n");
            Check(log.ReadTail(log.ActivePath).EndsWith("last\n") && log.ReadTail(log.ActivePath).Length < 513 * 1024, "bounded viewer tail");
            var blocked = Path.Combine(directory, "not-a-folder"); File.WriteAllText(blocked, "x");
            var failing = new RaceLogStore(blocked, () => now);
            failing.Write("TEST", "does not crash transfers");
            Check(failing.LastError is not null, "write failure reported without throwing");
            var callbacks = 0;
            var catcher = new IrcCatcher(onAnnouncement: _ => callbacks++);
            var routing = new IrcRouting([], [], [new(1, "Site", "Net", "#c", ["Bot"], "PRE", ["PRE"])]);
            catcher.Match(routing, "Net", "#c", "Bot", "PRE Release-GROUP");
            Check(callbacks == 0, "dry run not logged");
            catcher.Match(routing, "Net", "#c", "Bot", "PRE Release-GROUP", true);
            catcher.Match(routing, "Net", "#c", "Bot", "PRE Release-GROUP", true);
            Check(callbacks == 1, "announcement logged once");
            Console.WriteLine($"PASS: {count} RACE log checks.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
