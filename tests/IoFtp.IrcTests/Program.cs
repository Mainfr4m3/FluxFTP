using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;

var assertions = 0;
if (args.FirstOrDefault() == "--upload-timestamp-checks") { await UploadTimestampChecks.Run(); return; }
if (args.FirstOrDefault() == "--site-health-checks") { await SiteHealthChecks.Run(); return; }
if (args.FirstOrDefault() == "--linux-backup-fixture") { WindowsLinuxBackupFixture.Write(args[1]); return; }
if (args.FirstOrDefault() == "--visi-slot-checks") { await VisionaryLiveSlotChecks.Run(); return; }
if (args.FirstOrDefault() == "--race-route-checks")
{
    var routes = new RaceRouteStatistics();
    Parallel.For(0, 1000, _ => routes.Record("TV", "Release", "LFG", "TIS"));
    routes.Record("tv", "release", "lfg", "tis");
    routes.Record("TV", "Release2", "LFG", "TIS");
    routes.Record("TV", "Release", "CR", "TIS");
    routes.Record("FILM", "Release", "LFG", "TIS");
    if (routes.Snapshot().Single(row => row.Source == "LFG").Races != 3 || routes.Snapshot().Sum(row => row.Races) != 4)
        throw new Exception("Race routes must deduplicate repeats, preserve section identity and distinguish sources");
    if (!routes.RecordResult("TIS", "Release", "Me", "!Me") || routes.RecordResult("tis", "release", "Me", "Me"))
        throw new Exception("Win results must normalize the FTP login prefix and ignore repeat results across source routes");
    routes.RecordResult("TIS", "Release2", "Other", "Me");
    if (routes.RecordResult("TIS", "Unknown", "Me", "Me") || routes.Wins().Single().Wins != 1 || routes.Wins().Single().Results != 2)
        throw new Exception("Only observed races with explicit first-place results can affect wins/results");
    Console.WriteLine("PASS: concurrent duplicate chains counted once; new releases, sections and alternative sources counted separately.");
    return;
}
if (args.FirstOrDefault() == "--site-priority-checks") { await SitePriorityChecks.Run(); return; }
if (args.FirstOrDefault() == "--peak-speed-checks")
{
    var directory = Path.Combine(AppContext.BaseDirectory, "peak-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var session = new SessionStatistics();
        Parallel.For(0, 1000, speed => session.RecordSpeed(speed));
        session.RecordSpeed(-1);
        session.RecordSpeed(10);
        if (session.PeakSpeedBytesPerSecond != 999 || new SessionStatistics().PeakSpeedBytesPerSecond != 0)
            throw new Exception("Session maximum or restart reset failed");
        var path = Path.Combine(directory, "total-statistics.json");
        File.WriteAllText(path, "{\"StartedAt\":\"2026-01-01T00:00:00Z\",\"Uploaded\":123,\"Completed\":5}");
        var totals = new TotalStatisticsStore(directory);
        if (totals.Snapshot().PeakSpeedBytesPerSecond != 0) throw new Exception("Legacy totals migration failed");
        totals.RecordSpeed(123456789);
        totals.RecordSpeed(100);
        totals.Complete("Download", 456);
        var saved = new TotalStatisticsStore(directory).Snapshot();
        if (saved.PeakSpeedBytesPerSecond != 123456789 || saved.Uploaded != 123 || saved.Downloaded != 456 || saved.Completed != 6)
            throw new Exception("Peak persistence changed totals or lost the record");
        File.WriteAllText(path, "invalid");
        if (new TotalStatisticsStore(directory).Snapshot().PeakSpeedBytesPerSecond != 123456789)
            throw new Exception("Backup lost peak record");
        Console.WriteLine("PASS: concurrent session maximum, session reset, legacy totals, persistent peak, totals preservation and backup recovery.");
    }
    finally { Directory.Delete(directory, true); }
    return;
}
if (args.FirstOrDefault() == "--irc-metrics-checks")
{
    var directory = Path.Combine(Path.GetTempPath(), "fluxftp-metrics-" + Guid.NewGuid().ToString("N"));
    try
    {
        var metrics = new IrcMetricsFile(directory);
        if (new IrcMetricsFile().MetricsPath != Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "irc-metrics.txt")))
            throw new Exception("Default metrics file must be beside the FluxFTP executable");
        var path = Path.Combine(directory, "irc-metrics.txt");
        metrics.Publish("FluxFTP - Session 1 GB - Total 2 GB");
        var parts = File.ReadAllText(path).TrimEnd('\n').Split('\t');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var stamp) ||
            Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - stamp) > 2 || parts[1] != "FluxFTP - Session 1 GB - Total 2 GB")
            throw new Exception("Metrics heartbeat or text invalid");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            metrics.Publish("Locked update must not throw");
        metrics.Publish("FluxFTP - Session 3 GB - Total 4 GB");
        if (!File.ReadAllText(path).Contains("Session 3 GB - Total 4 GB")) throw new Exception("Did not recover after locked file");
        var blocked = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocked, "blocked");
        new IrcMetricsFile(blocked).Publish("Unwritable directory must not throw");
        var app = Path.Combine(directory, "FluxFTP portable å, test");
        var discovery = Path.Combine(directory, "discovery");
        var portable = new IrcMetricsFile(app, discovery);
        portable.Publish("FluxFTP - Session 5 GB - Total 6 GB");
        var pointer = Path.Combine(discovery, "irc-metrics.path");
        if (!File.ReadAllText(portable.MetricsPath).Contains("Session 5 GB") || File.ReadAllText(pointer).TrimEnd('\r', '\n') != portable.MetricsPath || File.Exists(Path.Combine(discovery, "irc-metrics.txt")))
            throw new Exception("Portable metrics path/discovery pointer invalid or heartbeat still saved in discovery directory");
        var noPointer = Path.Combine(directory, "after-recovery");
        var recover = new IrcMetricsFile(app, noPointer);
        using (var locked = new FileStream(portable.MetricsPath, FileMode.Open, FileAccess.Read, FileShare.None))
            recover.Publish("Locked portable heartbeat must not publish a pointer");
        if (File.Exists(Path.Combine(noPointer, "irc-metrics.path"))) throw new Exception("Discovery published before a successful heartbeat write");
        recover.Publish("FluxFTP - Session 7 GB - Total 8 GB");
        if (File.ReadAllText(Path.Combine(noPointer, "irc-metrics.path")).TrimEnd('\r', '\n') != portable.MetricsPath)
            throw new Exception("Portable discovery did not recover after a locked heartbeat");
        var writer = new IrcMetricsFile(app, discovery);
        using (var locked = new FileStream(pointer, FileMode.Open, FileAccess.Read, FileShare.None))
            writer.Publish("FluxFTP - Session 9 GB - Total 10 GB");
        if (!File.ReadAllText(writer.MetricsPath).Contains("Session 9 GB")) throw new Exception("Locked discovery pointer prevented metrics update");
        writer.Publish("FluxFTP - Session 11 GB - Total 12 GB");
        var moved = new IrcMetricsFile(Path.Combine(directory, "New FluxFTP folder"), discovery);
        moved.Publish("FluxFTP - Session 13 GB - Total 14 GB");
        if (File.ReadAllText(pointer).TrimEnd('\r', '\n') != moved.MetricsPath) throw new Exception("Discovery did not follow relocated FluxFTP folder");
        File.Delete(pointer);
        moved.Publish("FluxFTP - Session 15 GB - Total 16 GB");
        if (!File.Exists(pointer)) throw new Exception("Deleted discovery pointer did not recover");
        if (Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories).Any()) throw new Exception("Heartbeat replacement left temporary files after lock recovery");
        Console.WriteLine("PASS: EXE-local heartbeat, portable paths with spaces/Unicode, folder discovery/relocation, atomic replacement, locked heartbeat/pointer handling and recovery.");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    return;
}
if (args.FirstOrDefault() == "--hard-reset-checks") { HardResetChecks.Run(); return; }
if (args.FirstOrDefault() == "--fxp-estimate-checks") { FxpEstimateChecks.Run(); return; }
if (args.FirstOrDefault() == "--server-stats-checks") { await ServerStatsChecks.Run(); return; }
if (args.FirstOrDefault() == "--metrics-debug-checks")
{
    foreach (var line in new[] { "VISIONARY parallel source Failed.Release: A -> B", "RACE prequeue: A -> B; matching targets=4", "RACE watch [A -> B] Failed.Release: active/queued=0; temporary failures=1" })
        if (VisionaryLogVisibility.Show(line, false) || !VisionaryLogVisibility.Show(line, true)) throw new Exception("Race diagnostics require Debug: " + line);
    foreach (var line in new[] { "VISIONARY queued FXP Release: A -> B (2 files)", "VISIONARY Release: A -> B FAILED: unreachable", "VISIONARY pipe failed: busy", "VISIONARY batch A -> B: ERROR missing site", "VISIONARY Release stopped: maximum lifetime", "VISIONARY WARNING live slots unavailable", "Transfer blocked [DUPE]: 553 File exists", "FXP timings: PRET 25ms | STOR 20ms" })
        if (!VisionaryLogVisibility.Show(Environment.NewLine + line, false)) throw new Exception("Essential log hidden: " + line);
    foreach (var line in new[] { "Skiplist match: Failed.Release | file", "Skip list: file.rar", "Existing target skipped: file.rar; policy=Skip.", "Smart resume waiting: file.rar", "FXP diagnostic:\r\n  Source: A\r\n  > RETR file.rar\r\n  Destination: B", "VISIONARY TIMING [123] | source-PRET", "\r\nvisionary timing [123] Failed.Release | source-worker-wait", "VISIONARY Release: A -> B watching source", "VISIONARY batch A -> B: OK ACCEPTED Release — queued or watching", "VISIONARY prequeue: A -> B; existing targets skipped=4", "VISIONARY live slots A: downloads=8", "VISIONARY batch A -> B: OK WATCH UPDATED Release" })
        if (VisionaryLogVisibility.Show(line, false) || !VisionaryLogVisibility.Show(line, true)) throw new Exception("Debug toggle failed: " + line);
    Console.WriteLine("PASS: Visionary timing requires Debug; queued transfers and errors remain visible."); return;
}
if (args.FirstOrDefault() == "--bridge-info-checks")
{
    var at = new DateTime(2026, 10, 21, 12, 35, 8);
    var stamped = FtpLogTimestamp.Format("\r\n12:35:08 [TIS] < 550 denied\r\nOVERWRITE : file.rar", at);
    if (stamped != "\r\n2026.10.21-12:35:08 [TIS] < 550 denied\r\n2026.10.21-12:35:08 OVERWRITE : file.rar")
        throw new Exception("FTP timestamps must preserve line endings and replace old time-only prefixes");
    if (FtpLogTimestamp.Format(stamped, at.AddDays(1)) != stamped)
        throw new Exception("Existing timestamps must not be duplicated or changed");

    var commandRouter = new SiteCommandLogRouter();
    if (commandRouter.Route("> USER !test") || commandRouter.Route("< 331 Password required"))
        throw new Exception("Login traffic must remain in FTP-LOG");
    foreach (var line in new[] { "> SITE USER test", "< 200-User info", "< Credits: 123", "< 200 End" })
        if (!commandRouter.Route(line)) throw new Exception("SITE command/reply must go to COMMANDS");
    if (commandRouter.Route("> LIST /") || commandRouter.Route("< 150 Opening data"))
        throw new Exception("SITE routing must reset at next FTP command");
    if (!commandRouter.Route("> site rules") || !commandRouter.Route("< 550 denied"))
        throw new Exception("SITE failure replies must stay with command");
    if (!VisionaryLogVisibility.IsCommandReport("SERVER STATS [TIS] SITE STATS test") ||
        VisionaryLogVisibility.IsCommandReport("SERVER DETAILS [file.rar] Transfer CRC failed"))
        throw new Exception("Command report classification failed");
    var failureSite = VisionaryLogVisibility.FailureSite("FTP 550 denied | site=TIS; command=RETR /file");
    if (failureSite != "TIS" || VisionaryLogVisibility.CompactTransferFailure("FTP", "550 denied", failureSite) != "TRANSFER FAILED (550) [TIS]")
        throw new Exception("550 summary must identify the replying site");

    var startup = new StartupMessage();
    if (string.IsNullOrWhiteSpace(startup.Text) || startup.Display("Queued") != startup.Text || startup.TransferStarted)
        throw new Exception("Startup message must remain visible before first transfer");
    startup.StartTransfer();
    if (startup.Display("Receiving") != "Receiving" || startup.Display("Idle") != "Idle")
        throw new Exception("Startup message must not reappear after any transfer starts");
    if (VisionaryLogVisibility.CompactTransferFailure("file.rar", "550 File locking failed") != "FILE LOCK FAILED : file.rar")
        throw new Exception("Lock failure summary incorrect");
    if (VisionaryLogVisibility.CompactTransferFailure("file.rar", "550 Permission denied").Contains("LOCK"))
        throw new Exception("550 permission denial must not be reported as a file lock");
    if (!VisionaryLogVisibility.Is550("< 550 File locking failed") || !VisionaryLogVisibility.Is550("FTP server replied 550: denied") || VisionaryLogVisibility.Is550("file.550.rar"))
        throw new Exception("550 detection must identify FTP replies, not filename numbers");
    if (VisionaryLogVisibility.CompactTransferFailure("file.rar", "very long error").Length > 40)
        throw new Exception("Generic transfer failure must be concise");

    if (VisionaryLogVisibility.UploadBusySummary("Failed job file.rar (553 File is being uploaded by waster).") != "UPLOADED by waster")
        throw new Exception("Upload-busy summary missing user");
    foreach (var message in new[] { "553 File exists", "553 Permission denied", "550 File is being uploaded by waster" })
        if (VisionaryLogVisibility.UploadBusySummary(message) is not null) throw new Exception("Other FTP failure misclassified as upload-busy");

    var denial = VisionaryLogVisibility.LoginFailureSummary("VISIONARY source FAILED Very.Long.Release: Race listing site=JBG; operation=connect/login: 530 Access denied | site=JBG; command=USER; retry in 30s");
    if (denial is not { Site: "JBG" } || !denial.Value.Text.Contains("LOGIN FAILED") || !denial.Value.Text.Contains("ACCESS DENIED") || !denial.Value.Text.Contains("BLOCKED") || denial.Value.Text.Contains("Very.Long.Release"))
        throw new Exception("Login failure must produce concise site/command summary");
    foreach (var message in new[] { "site=JBG; command=LIST; 530 Access denied", "site=JBG; command=USER; 550 denied", "site=JBG; command=CWD; 530 denied", "VISIONARY Release 530: waiting" })
        if (VisionaryLogVisibility.LoginFailureSummary(message) is not null) throw new Exception("Non-login failure misclassified");

    foreach (var message in new[] { "VISIONARY source FAILED Release: denied", "\r\nVISIONARY queued FXP Release", "VISIONARY TIMING [id]", "RACE slots for JBG: login rejected", "RACE watch [A -> B] waiting", "BRIDGE alternate: warning" })
        if (!VisionaryLogVisibility.IsBridgeMessage(message)) throw new Exception("Missing bridge route: " + message);
    foreach (var message in new[] { "Transfer completed: file", "Transfer failed: file", "FXP timings: PRET 1ms", "> USER !name", "SERVER STATS [A]", "Connecting to A" })
        if (VisionaryLogVisibility.IsBridgeMessage(message)) throw new Exception("FTP message misrouted: " + message);
    var directory = Path.Combine(Path.GetTempPath(), "FluxBridge-" + Guid.NewGuid());
    try
    {
        var bridge = new RaceLogStore(directory, name: "Bridge-Info");
        var race = new RaceLogStore(directory);
        bridge.Write("BRIDGE", "source FAILED"); race.Write("TRANSFER", "completed");
        if (bridge.ActivePath == race.ActivePath || !bridge.ReadTail(bridge.ActivePath).Contains("source FAILED") || race.ReadTail(race.ActivePath).Contains("source FAILED"))
            throw new Exception("Bridge and race stores must remain separate");
        if (bridge.ArchivePattern == race.ArchivePattern) throw new Exception("Bridge archive filter must be separate");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    Console.WriteLine("PASS: bridge warnings/status routed separately from FTP, with independent stores and archive filters.");
    return;
}
if (args.FirstOrDefault() == "--race-log-checks") { RaceLogChecks.Run(); return; }
if (args.FirstOrDefault() == "--udp-control-checks") { await UdpControlChecks.Run(); return; }
if (args.FirstOrDefault() == "--chain-batch-checks") { await ChainBatchChecks.Run(); return; }
if (args.FirstOrDefault() == "--skip-filter-mode-checks") { SkipFilterModeChecks.Run(); return; }
if (args.FirstOrDefault() == "--skip-match-checks") { SkipMatchChecks.Run(); return; }
if (args.FirstOrDefault() == "--race-empty-source-checks") { RaceEmptySourceChecks.Run(); return; }
if (args.FirstOrDefault() == "--resume-recovery-checks") { await ResumeRecoveryChecks.Run(); return; }
if (args.FirstOrDefault() == "--global-port-checks") { GlobalPortChecks.Run(); return; }
if (args.FirstOrDefault() == "--passive-port-checks") { await PassivePortChecks.Run(); return; }
if (args.FirstOrDefault() == "--transfer-policy-checks") { await TransferPolicyChecks.Run(); return; }
if (args.FirstOrDefault() == "--fxp-metrics-checks")
{
    var time = TimeSpan.Zero;
    var stats = new SessionStatistics(() => time);
    stats.Complete("FXP", 6000); stats.Complete("FXP", 6000); stats.Complete("Download", 90000);
    if (stats.FxpRecentRate != 200) throw new Exception("Parallel completions or non-FXP counted incorrectly");
    time = TimeSpan.FromSeconds(30);
    if (stats.FxpSessionRate != 400) throw new Exception("Session average");
    stats.Complete("FXP", 6000);
    if (stats.FxpRecentRate != 300) throw new Exception("Rolling sum");
    time = TimeSpan.FromSeconds(60);
    if (stats.FxpRecentRate != 100) throw new Exception("Boundary expiration");
    time = TimeSpan.FromSeconds(90);
    if (stats.FxpRecentRate != 0 || stats.Fxp != 18000 || stats.FxpSessionRate != 200) throw new Exception("Idle expiry or session totals lost");
    Console.WriteLine("PASS: passive Metrics parallel completions, FXP-only counting, 60-second expiry and session average."); return;
}
if (args.FirstOrDefault() == "--passive-fxp-checks")
{
    if (PassiveFxpAverage.Calculate(50000000, TimeSpan.FromSeconds(2)) != 25000000) throw new Exception("File average");
    if (PassiveFxpAverage.Calculate(5000000, TimeSpan.FromSeconds(2)) != 2500000) throw new Exception("Transferred portion average");
    if (PassiveFxpAverage.Calculate(0, TimeSpan.FromSeconds(2)) is not null || PassiveFxpAverage.Calculate(-1, TimeSpan.FromSeconds(2)) is not null || PassiveFxpAverage.Calculate(10, TimeSpan.Zero) is not null) throw new Exception("Unknown/invalid measurement");
    if (PassiveFxpAverage.Calculate(long.MaxValue, TimeSpan.FromTicks(1)) != long.MaxValue) throw new Exception("Overflow");
    await SecureReverseFxpChecks.Run();
    Console.WriteLine("PASS: passive per-file averages, unknown sizes, zero durations, overflow and successful FXP completion timing."); return;
}
if (args.FirstOrDefault() == "--zero-slot-checks") { await ZeroSlotChecks.Run(); return; }
if (args.FirstOrDefault() == "--site-compatibility-checks") { await SiteCompatibilityChecks.Run(); return; }
if (args.FirstOrDefault() == "--race-timing-checks")
{
    long tick = 1000;
    var lines = new List<string>();
    var trace = new RaceStartTrace("test", 1000, 900, 950, lines.Add, () => tick);
    if (!lines[0].Contains("chain-to-dll=50ms; dll-to-pipe=50ms")) throw new Exception("Bridge timing");
    tick = 1200; trace.Mark("watch-admitted"); trace.Mark("watch-admitted");
    if (lines.Count != 2 || !lines[1].Contains("since-chain=300ms")) throw new Exception("Phase timing/deduplication");
    trace.Protocol("source", "> PASS secret", "job");
    trace.Protocol("source", "> RETR file", "job");
    if (lines.Count != 3 || lines.Any(line => line.Contains("secret"))) throw new Exception("Credential filtering");
    var unknown = new RaceStartTrace("old", 1000, 0, 0, lines.Add, () => tick);
    if (!lines[^1].Contains("since-chain=unknown")) throw new Exception("Old bridge");
    var future = new RaceStartTrace("bad", 1000, 1100, 1200, lines.Add, () => tick);
    if (!lines[^1].Contains("dll-to-pipe=unknown")) throw new Exception("Future timestamps");
    new RaceStartTrace("safe", 1000, 900, 950, _ => throw new Exception("logger"), () => tick).Mark("safe");
    Console.WriteLine("RACE timing: 7 checks passed."); return;
}
if (args.FirstOrDefault() == "--race-done-checks") { RaceDoneChecks.Run(); return; }
if (args.FirstOrDefault() == "--backup-checks") { BackupChecks.Run(); return; }
if (args.FirstOrDefault() == "--visionary-rule-checks") { VisionaryRuleChecks.Run(args[1]); return; }
if (args.FirstOrDefault() == "--secure-reverse-fxp-checks") { await SecureReverseFxpChecks.Run(); return; }
if (args.FirstOrDefault() == "--openssl-checks") { await OpenSslChecks.Run(); return; }
if (args.FirstOrDefault() == "--path-editor-checks")
{
    PathEditorChecks.Run(args[1]);
    return;
}
if (args.FirstOrDefault() == "--delete-checks")
{
    await RemoteDeletionChecks.Run();
    return;
}
if (args.FirstOrDefault() == "--fxp-monitor-checks")
{
    FxpSpeedChecks.Run();
    await FxpProgressProbeChecks.Run();
    return;
}
if (args.FirstOrDefault() == "--file-priority-checks")
{
    await TransferFilePriorityChecks.Run();
    GlobalSettingsChecks.Run();
    return;
}
if (args.FirstOrDefault() == "--binary-mode-checks")
{
    await BinaryModeChecks.Run();
    return;
}
if (args.FirstOrDefault() == "--race-listing-checks")
{
    VisionarySlotChecks.Run();
    await RaceListingChecks.Run();
    return;
}
FxpSpeedChecks.Run();
if (args.FirstOrDefault() == "--import-local-checks")
{
    ProfileStoreChecks.Run();
    FtpRushMigrationChecks.Run();
    await LocalTransferChecks.Run();
    foreach (var folder in args.Skip(1))
    {
        var files = VisionaryImportInventory.ReadFolder(folder);
        var profile = new ConnectionProfile(Guid.NewGuid(), "ARES", "example.invalid", 21, "test", TransferProtocol.FtpsExplicit);
        var imported = VisionarySiteMigration.Apply(profile, files.Select(file => file.Path));
        Console.WriteLine($"VISIONARY: {files.Count} configuration files, {imported.EffectiveOptions.Affils.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length} affils, {imported.EffectiveOptions.VisionaryRules.Length} rule characters imported in memory; originals unchanged.");
    }
    return;
}
if (args.FirstOrDefault() == "--race-flow-checks")
{
    await RaceFlowChecks.Run();
    return;
}
if (args.FirstOrDefault() == "--rush-migration-checks")
{
    FtpRushMigrationChecks.Run();
    foreach (var path in args.Skip(1))
    {
        var package = FtpRushSiteImporter.ImportPackage(path);
        var rows = FtpRushSectionMigration.Preview(package.Bookmarks);
        var ambiguous = package.Sites.GroupBy(site => site.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(row => ambiguous.Contains(row.Site))) row.Selected = false;
        var names = package.Sites.Select(site => site.Profile.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
        var sections = FtpRushSectionMigration.Merge([], rows, names, false);
        Console.WriteLine($"Read {Path.GetFileName(path)}: {package.Sites.Count} sites, {package.Bookmarks.Count} bookmarks, {rows.Count} section candidates, {rows.Count(row => row.Selected)} selected paths, {sections.Count} merged sections; no files written.");
    }
    return;
}
if (args.FirstOrDefault() == "--visionary-checks")
{
    VisionaryConfigurationChecks.Run();
    VisionaryRaceChecks.Run();
    VisionaryPayloadChecks.Run();
    foreach (var path in args.Skip(1))
    {
        var profiles = VisionaryTransferProfiles.Load(path);
        Console.WriteLine($"Validated sample/covers profiles from {Path.GetFileName(path)}; original unchanged.");
    }
    return;
}
if (args.FirstOrDefault() == "--visionary-files")
{
    foreach (var path in args.Skip(1))
    {
        var file = new VisionaryConfiguration(path);
        if (file.HasChanges) throw new Exception("Imported file incorrectly marked changed.");
        Console.WriteLine($"Parsed {Path.GetFileName(path)}: {file.Entries.Count} entries; original unchanged.");
    }
    return;
}
if (!IrcConnectionError.RegistrationReply("464", "secret")!.Contains("password rejected") ||
    !IrcConnectionError.RegistrationReply("433", "secret")!.Contains("Nickname") ||
    IrcConnectionError.RegistrationReply("ERROR", "password secret")!.Contains("secret") ||
    IrcConnectionError.Describe(new System.IO.IOException("secret")).Contains("secret")) throw new Exception("IRC connection diagnostics failed");
var projectLines = IrcProjectNews.Parse("""
{"draft":false,"tag_name":"v1.0.53","published_at":"2026-09-29T12:00:00Z","prerelease":false}
""");
if (projectLines.Length != 1 || !projectLines[0].Contains("2026-09-29 12:00 UTC") || !projectLines[0].Contains("release v1.0.53") || !projectLines[0].EndsWith("Mainfr4m3/FluxFTP/releases/tag/v1.0.53")) throw new Exception("Project news parse failed");
ProfileStoreChecks.Run();
GlobalSettingsChecks.Run();
VisionaryConfigurationChecks.Run();
VisionaryRaceChecks.Run();
VisionaryPayloadChecks.Run();
FtpRushMigrationChecks.Run();
var ircApiRequest = new IrcConnectRequest("leon.example.test", 29025, "FLUXFTP", "#FLUX",
    UseZnc: true, ZncUsername: "FLUX", ZncNetwork: "FreakNET", Password: "password:with:colons");
var ircApiSettings = ircApiRequest.Apply(new IrcSettings(Host: "other.example.test", AccountLinks: [new("old", "admin")]));
if (!ircApiSettings.Enabled || ircApiSettings.Password != "password:with:colons" || ircApiSettings.AccountLinks!.Length != 0 || ircApiSettings.ZncNetwork != "FreakNET") throw new Exception("IRC API settings conversion failed");
foreach (var invalid in new[] { ircApiRequest with { Port = 0 }, ircApiRequest with { Host = "host\r\nPASS bad" }, ircApiRequest with { Channel = "#a,#b" }, ircApiRequest with { Password = "bad\npassword" }, ircApiRequest with { ZncUsername = "" } })
{
    var rejected = false;
    try { invalid.Apply(null); } catch (ArgumentException) { rejected = true; }
    if (!rejected) throw new Exception("Invalid IRC API request accepted");
}
RaceLogChecks.Run();
await IrcRoutingChecks.Run();
await IrcConnectionChecks.Run();
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    assertions++;
}

Check(IrcProtocol.IsAdmin(new(200, "200-Flags: 1A\r\n200 End")), "SiteOp");
Check(IrcProtocol.IsAdmin(new(200, "200-| UserName........: khaz              Flags.............: 31MTHL         |\r\n200 Command successful.")), "ioFTPD table admin");
Check(!IrcProtocol.IsAdmin(new(200, "200-| UserName........: khaz              Flags.............: 3THL         |")), "ioFTPD table non-admin");
Check(!IrcProtocol.IsAdmin(new(200, "200-| Tagline.........: Flags.............: 1M |")), "ioFTPD table tagline cannot grant authority");
Check(!IrcProtocol.IsAdmin(new(200, "200-| UserName........: khaz Flags.............: 31MTHL |\r\n200-Flags: 3")), "mixed duplicate flags rejected");
Check(!IrcProtocol.IsAdmin(new(550, "550-| UserName........: khaz Flags.............: 31MTHL |")), "ioFTPD table failure cannot grant authority");
Check(IrcProtocol.IsAdmin(new(200, "200-\u001b[31mFlags: M\u001b[0m")), "Master with ANSI color");
Check(!IrcProtocol.IsAdmin(new(200, "Flags: V3\nUser: Master1")), "non-admin");
Check(!IrcProtocol.IsAdmin(new(550, "Flags: 1")), "FTP error is not authority");
Check(!IrcProtocol.IsAdmin(new(200, "Tagline: Flags: M\nFlags: 3")), "tagline is not authority");
Check(!IrcProtocol.IsAdmin(new(200, "Flags: 1\nFlags: 3")), "ambiguous flags rejected");
Check(!IrcProtocol.FtpUser("user\r\nSITE SHUTDOWN") && !IrcProtocol.FtpUser("*"), "FTP injection and wildcard rejected");
var parsed = IrcMessage.Parse("@account=admin;time=2026-09-28T12:00:00Z :nick!u@h PRIVMSG FluxFTP :!login bob password with spaces");
Check(parsed.Nick == "nick" && parsed.Parameters[1] == "!login bob password with spaces" && parsed.Tags["account"] == "admin", "IRC parser");
var now = DateTimeOffset.UtcNow;
Check(!IrcProtocol.IsLive(parsed, now, true), "old history ignored");
Check(!IrcProtocol.IsLive(IrcMessage.Parse(":n!u@h PRIVMSG #test :!op"), now, true), "ZNC requires timestamps");
Check(!IrcProtocol.IsLive(IrcMessage.Parse($"@batch=old;time={now:O} :n!u@h PRIVMSG #test :!op"), now, false), "batches ignored");
Check(IrcProtocol.Same("Test[User]", "test{user}"), "IRC casemapping");

var protect = typeof(GlobalSettingsStore).GetMethod("Protect", BindingFlags.NonPublic | BindingFlags.Static)!;
var unprotect = typeof(GlobalSettingsStore).GetMethod("UnprotectValue", BindingFlags.NonPublic | BindingFlags.Static)!;
var encrypted = (string)protect.Invoke(null, ["znc-secret"])!;
Check(encrypted.StartsWith("dpapi:") && !encrypted.Contains("znc-secret") && (string)unprotect.Invoke(null, [encrypted])! == "znc-secret", "password protection roundtrip");
Check(System.Text.Json.JsonSerializer.Deserialize<GlobalSettings>("{}")!.Irc is null, "old settings compatible");

var site = new ConnectionProfile(Guid.NewGuid(), "Test", "unused", 21, "lookup", TransferProtocol.FtpsExplicit, "lookup-secret");
var backend = new FakeBackend();
Check(await IrcFtpAccess.VerifyAsync(site, "admin", "correct password", backend.Create, default), "private login checks password and flags");
Check(backend.Logins.SequenceEqual(new[] { "admin", "lookup" }), "login identity then independent authority lookup");
backend.Logins.Clear();
try { await IrcFtpAccess.VerifyAsync(site, "admin", "wrong", backend.Create, default); Check(false, "wrong password throws"); }
catch (UnauthorizedAccessException) { Check(backend.Logins.Count == 1, "wrong password never reaches flag lookup"); }
Check(!await IrcFtpAccess.VerifyAsync(site, "regular", "correct password", backend.Create, default), "valid password without admin denied");
Check(!await IrcFtpAccess.VerifyAsync(site with { Protocol = TransferProtocol.Ftp }, "admin", "correct password", backend.Create, default), "plaintext FTP denied");
Check(!await IrcFtpAccess.VerifyAsync(site with { AllowInvalidCertificate = true }, "admin", null, backend.Create, default), "unverified FTPS denied");
Check(await IrcFtpAccess.VerifyAsync(site, "admin", null, backend.Create, default), "linked account checked against current flags");

using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var options = new IrcSettings(Enabled: true, Host: "127.0.0.1", Port: ((IPEndPoint)listener.LocalEndpoint).Port,
    UseTls: false, Channel: "#test", AdminSiteId: site.Id, AllowPrivateLogin: false,
    AccountLinks: [new("admin-account", "admin"), new("regular-account", "regular")]);
var logs = new List<string>();
await using (var bot = new IrcService(new GlobalSettings(Irc: options), s => { lock (logs) logs.Add(s); }, () => site, backend.Create,
    projectNews: _ => Task.FromResult<IReadOnlyList<string>>(["FluxFTP test build: https://github.com/Mainfr4m3/FluxFTP/releases"])))
{
    using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
    using var reader = new StreamReader(client.GetStream());
    using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
    async Task<string> Read() => (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5))) ?? throw new Exception("Disconnected");
    async Task Until(string expected)
    {
        for (var i = 0; i < 12; i++) if ((await Read()).Contains(expected, StringComparison.Ordinal)) return;
        throw new Exception("Missing reply: " + expected);
    }
    Check(await Read() == "CAP LS 302", "capability negotiation starts");
    await Until("USER fluxftp");
    await writer.WriteLineAsync(":server CAP FluxFTP LS :account-tag account-notify extended-join server-time batch");
    await Until("CAP REQ");
    await writer.WriteLineAsync(":server CAP FluxFTP ACK :account-tag account-notify extended-join server-time batch");
    await Until("CAP END");
    await writer.WriteLineAsync(":server 001 FluxFTP :Welcome");
    await Until("JOIN #test");

    await writer.WriteLineAsync(":admin-account!u@h PRIVMSG #test :!op");
    await Until("No verified account");
    Check(true, "nickname alone denied");
    await Task.Delay(1100);
    await writer.WriteLineAsync("@account=admin-account :legit!u@h PRIVMSG #test :!op");
    await Until("MODE #test +o legit");
    await Until("FTP admin verified");
    Check(true, "verified mapped account granted");
    await Task.Delay(1100);
    await writer.WriteLineAsync("@account=regular-account :regular!u@h PRIVMSG #test :!op");
    await Until("MODE #test -o regular");
    await Until("FTP admin verification failed");
    Check(true, "mapped non-admin denied");
    await Task.Delay(1100);
    await writer.WriteLineAsync(":public!u@h PRIVMSG #test :!login admin secret");
    await Until("private message");
    Check(true, "public credential command rejected");
    await Task.Delay(1100);
    var loginsBeforeNews = backend.Logins.Count;
    await writer.WriteLineAsync(":news!u@h PRIVMSG #test :!news");
    await Until("PRIVMSG #test :FluxFTP test build: https://github.com/Mainfr4m3/FluxFTP/releases");
    Check(backend.Logins.Count == loginsBeforeNews, "project news replies in channel without FTP login");
    await Task.Delay(1100);

    backend.WaitForLookup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    backend.LookupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await writer.WriteLineAsync("@account=admin-account :changing!u@h PRIVMSG #test :!op");
    await backend.LookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await writer.WriteLineAsync(":changing!u@h NICK :changed");
    await writer.WriteLineAsync("PING :during-ftp");
    Check(await Read() == "PONG :during-ftp", "PING handled during slow FTP check");
    backend.WaitForLookup.SetResult();
    await Task.Delay(200);
    await writer.WriteLineAsync("PING :after-nick");
    Check(await Read() == "PONG :after-nick", "no grant after identity change");

    await writer.WriteLineAsync("@batch=history;account=admin-account :replay!u@h PRIVMSG #test :!op");
    await writer.WriteLineAsync("PING :after-replay");
    Check(await Read() == "PONG :after-replay", "history cannot grant operator");
    await writer.WriteLineAsync("@account=admin-account :setup!u@h PRIVMSG FluxFTP :!slots Test 2");
    await writer.WriteLineAsync("@account=admin-account :setup!u@h PRIVMSG FluxFTP :!maxupdn Test 2 1");
    await writer.WriteLineAsync("@account=admin-account :setup!u@h PRIVMSG FluxFTP :!setdir Test MP3 /MP3");
    for (var i = 0; i < 3; i++)
        Check((await Read()).Contains("Setup commands require a private message and a TLS"), "queued setup lines processed in order, denied without TLS");
}
Check(logs.All(log => !log.Contains("secret") && !log.Contains("correct password")), "logs contain no passwords");
await using (var bot = new IrcService(new GlobalSettings(Irc: options with { UseZnc = true, ZncUsername = "zncuser", ZncNetwork = "network", Password = "znc-secret" }), _ => { }, () => site, backend.Create))
{
    using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
    using var reader = new StreamReader(client.GetStream());
    using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
    async Task<string> Read() => (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5))) ?? throw new Exception("Disconnected");
    Check(await Read() == "PASS :zncuser/network:znc-secret", "ZNC PASS format");
    Check(await Read() == "CAP LS 302", "ZNC capability negotiation after password");
    await Read(); await Read();
    await writer.WriteLineAsync(":server CAP FluxFTP LS :account-tag extended-join server-time batch");
    await Read();
    await writer.WriteLineAsync(":server CAP FluxFTP ACK :account-tag extended-join server-time batch");
    await Read();
    await writer.WriteLineAsync(":server 001 FluxFTP :Welcome");
    await Read();
    await writer.WriteLineAsync("@account=admin-account :missingtime!u@h PRIVMSG #test :!op");
    await writer.WriteLineAsync("PING :no-time");
    Check(await Read() == "PONG :no-time", "ZNC ignores messages without timestamp");
    await writer.WriteLineAsync($"@time={DateTimeOffset.UtcNow.AddHours(-1):O};account=admin-account :old!u@h PRIVMSG #test :!op");
    await writer.WriteLineAsync("PING :old-time");
    Check(await Read() == "PONG :old-time", "ZNC ignores old playback");
    await writer.WriteLineAsync($"@time={DateTimeOffset.UtcNow:O};batch=history;account=admin-account :batch!u@h PRIVMSG #test :!op");
    await writer.WriteLineAsync("PING :batched");
    Check(await Read() == "PONG :batched", "ZNC ignores recent batched playback");
    await writer.WriteLineAsync($"@time={DateTimeOffset.UtcNow:O};account=admin-account :live!u@h PRIVMSG #test :!op");
    Check(await Read() == "MODE #test +o live", "ZNC accepts live verified command");
    await Read();
    await Task.Delay(1100);
    await writer.WriteLineAsync($"@time={DateTimeOffset.UtcNow:O} :joined!u@h JOIN #test admin-account :Real Name");
    Check(await Read() == "MODE #test +o joined", "extended JOIN automatically verifies linked admin");
}
Console.WriteLine($"PASS: {assertions} IRC checks, including simulated-server integration.");
SiteRuleChecks.Run();
IrcSetupChecks.Run();

sealed class FakeBackend
{
    public List<string> Logins { get; } = [];
    public TaskCompletionSource? WaitForLookup { get; set; }
    public TaskCompletionSource? LookupStarted { get; set; }
    public IRemoteSession Create() => new Session(this);
    private sealed class Session(FakeBackend owner) : IRemoteSession
    {
        public bool IsConnected => true;
        public IReadOnlySet<string> Capabilities => new HashSet<string>();
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            owner.Logins.Add(profile.Username);
            if (profile.Username != "lookup" && profile.Password != "correct password") throw new UnauthorizedAccessException();
            return Task.CompletedTask;
        }
        public async Task<RemoteCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
        {
            owner.LookupStarted?.TrySetResult();
            if (owner.WaitForLookup is { } pending) await pending.Task.WaitAsync(cancellationToken);
            return new(200, command == "SITE USER admin" ? "200-Flags: 1M" : "200-Flags: 3");
        }
        public Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RemoteEntry>>([new("Newest.Release", "/Newest.Release", true, null, DateTimeOffset.UtcNow)]);
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task DownloadAsync(string remotePath, Stream destination, long offset, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UploadAsync(string remotePath, Stream source, long offset, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
