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
            var exported = Path.Combine(directory, "export.txt");
            log.ExportAsync(log.ActivePath, exported).GetAwaiter().GetResult();
            Check(File.ReadAllText(exported) == File.ReadAllText(log.ActivePath), "TXT export preserves full log");
            using (var large = new FileStream(log.ActivePath, FileMode.Open, FileAccess.Write)) large.SetLength(RaceLogStore.MaxBytes);
            log.Write("TEST", "after-cap-reset");
            Check(new FileInfo(log.ActivePath).Length < 1024 && File.ReadAllText(log.ActivePath).Contains("after-cap-reset"), "100 MB clears active log and retains next event");
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
            var commands = new RaceLogStore(directory, () => now, name: "Commands", preserveLineBreaks: true);
            var response = "226-.----== ioNiNJA ==----.\r\n226-| CRC-Check: oK!       |\r\n226-+-=[UserTop]=---------+\r\n226-|  1. TestUser 15.4GB  |\r\n226-`---------------------'\r\n226 [ UL: 474 GB ] [ DL: 0 MB ]";
            var report = ServerTransferReport.Statistics(response)!;
            commands.Write("COMMAND", "SERVER STATS [destination] release.r55\r\n" + report);
            var text = commands.ReadTail(commands.ActivePath);
            var table = report.Replace("\r\n", Environment.NewLine);
            Check(text.Contains("release.r55" + Environment.NewLine + table, StringComparison.Ordinal), "COMMANDS retains full transfer stats table after heading");
            Check(text.Split(Environment.NewLine).Contains("|  1. TestUser 15.4GB  |"), "COMMANDS preserves table spaces, borders and column alignment");
            Check(!text.Contains("226-") && !text.Contains("r55.----"), "cleaned server reply stays on separate lines");
            commands.Write("COMMAND", "[TEST] > SITE WKUP");
            commands.Write("COMMAND", "[TEST] < 200 Command successful.");
            var stamp = now.ToString("O");
            Check(commands.ReadTail(commands.ActivePath).EndsWith($"{stamp} [COMMAND] [TEST] > SITE WKUP{Environment.NewLine}{stamp} [COMMAND] [TEST] < 200 Command successful.{Environment.NewLine}"), "manual SITE command/reply formatting stays unchanged");
            commands.Write("COMMAND\0", "mixed\r\n  row one\n\trow two\rrow three\0\u0007");
            text = commands.ReadTail(commands.ActivePath);
            Check(text.Contains("mixed" + Environment.NewLine + "  row one" + Environment.NewLine + "\trow two" + Environment.NewLine + "row three"), "COMMANDS normalizes CRLF/LF/CR while retaining indentation");
            Check(!text.Contains('\0') && !text.Contains('\u0007'), "COMMANDS removes unrelated control characters");
            Parallel.For(0, 50, i => commands.Write("COMMAND", $"block-{i}\n  middle-{i}\nend-{i}"));
            text = commands.ReadTail(commands.ActivePath);
            Check(Enumerable.Range(0, 50).All(i => text.Contains($"block-{i}{Environment.NewLine}  middle-{i}{Environment.NewLine}end-{i}{Environment.NewLine}")), "concurrent multiline reports remain contiguous");
            var commandExport = Path.Combine(directory, "commands-export.txt");
            commands.ExportAsync(commands.ActivePath, commandExport).GetAwaiter().GetResult();
            Check(File.ReadAllBytes(commandExport).SequenceEqual(File.ReadAllBytes(commands.ActivePath)), "COMMANDS export preserves multiline table bytes");
            now = now.AddHours(24); commands.Maintain();
            var commandArchive = Directory.GetFiles(commands.ArchiveDirectory, commands.ArchivePattern).Single();
            Check(File.ReadAllText(commandArchive).Contains(table), "COMMANDS rotation preserves multiline reports in its own archive");
            using (var large = new FileStream(commands.ActivePath, FileMode.Open, FileAccess.Write)) large.SetLength(RaceLogStore.MaxBytes);
            commands.Write("COMMAND", "SERVER STATS\n" + report);
            Check(new FileInfo(commands.ActivePath).Length < 1024 && commands.ReadTail(commands.ActivePath).Contains(table), "COMMANDS size cap resets without flattening next report");
            Console.WriteLine($"PASS: {count} RACE log checks.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
