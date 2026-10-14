using System.IO;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;

internal static class GlobalSettingsChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-settings-" + Guid.NewGuid());
        var count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("Global settings: " + name); count++; }
        try
        {
            var irc = new IrcSettings(Enabled: true, Host: "irc.example.test", Port: 6697, UseTls: true,
                Nick: "FluxTest", Password: "dpapi:literal-password %20", UseZnc: true, ZncUsername: "user",
                ZncNetwork: "network", Channel: "#test", NetworkName: "Example", AccountLinks: [new("account", "admin")], AllowInvalidCertificate: false);
            var settings = new GlobalSettings(UseAdvancedSkipFilters: true, LogSkipListMatches: true, OverwriteIfResumeFails: true, ActivePortFrom: 49000, ActivePortTo: 49100, PassivePortMin: 50000, PassivePortMax: 51000, FxpSmallerAction: IoFtp.Core.Models.ExistingTargetAction.Resume, FxpSameAction: IoFtp.Core.Models.ExistingTargetAction.Overwrite, AsciiRules: "*.TXT\nregex:^README$", NoAsciiResume: false, Irc: irc, TransferCategoryOrder: "NFO\nSFV\nMain files\nProof\nSample");
            new GlobalSettingsStore(directory).Save(settings);
            var path = Path.Combine(directory, "settings.json");
            Check(!File.ReadAllText(path).Contains(irc.Password), "secret protected on disk");
            var restored = new GlobalSettingsStore(directory).Load();
            Check(restored.UseAdvancedSkipFilters == true, "advanced skip mode survives restart");
            Check(restored.LogSkipListMatches, "skip match logging preference survives restart");
            Check(restored.OverwriteIfResumeFails, "resume overwrite preference survives restart");
            Check(new GlobalSettings().OverwriteIfResumeFails && new GlobalSettings().DefaultSlots == 2 && new GlobalSettings().DefaultUploadSlots == 1 && new GlobalSettings().DefaultDownloadSlots == 1, "Rush-compatible fresh defaults");
            var oldJson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            oldJson.Remove("ResumeOverwriteDefaultsApplied"); oldJson["OverwriteIfResumeFails"] = false;
            File.WriteAllText(path, oldJson.ToJsonString());
            Check(new GlobalSettingsStore(directory).Load().OverwriteIfResumeFails, "older default-off version migrates to standard-on");
            new GlobalSettingsStore(directory).Save(settings with { OverwriteIfResumeFails = false });
            Check(!new GlobalSettingsStore(directory).Load().OverwriteIfResumeFails, "explicit disable is retained after migration");
            new GlobalSettingsStore(directory).Save(settings);
            Check(restored.ActivePortFrom == 49000 && restored.ActivePortTo == 49100 && restored.PassivePortMin == 50000 && restored.PassivePortMax == 51000, "global active/passive ports survive restart");
            Check(restored.FxpSmallerAction == settings.FxpSmallerAction && restored.FxpSameAction == settings.FxpSameAction && restored.AsciiRules == settings.AsciiRules && !restored.NoAsciiResume, "conflict actions and ASCII rules survive restart");
            Check(new GlobalSettingsStore(directory).Load().TransferCategoryOrder == settings.TransferCategoryOrder, "transfer category order survives restart");
            var layout = new Dictionary<string, ViewLayoutSettings>
            {
                ["Left"] = new(["Attributes", "Status"], "SortSize", true, "Consolas", 14, true),
                ["Right"] = new(["Modified"], "Name", false),
                ["Queue"] = new(["Source"], "ProgressPercent", true)
            };
            new GlobalSettingsStore(directory).Save(settings with { Layout = layout });
            var savedLayout = new GlobalSettingsStore(directory).Load().Layout!;
            Check(savedLayout["Left"].HiddenColumns!.SequenceEqual(layout["Left"].HiddenColumns!), "hidden columns survive restart");
            Check(savedLayout["Left"].Descending && savedLayout["Left"].SortBy == "SortSize", "sort property and direction survive restart");
            Check(savedLayout["Left"].FontFamily == "Consolas" && savedLayout["Left"].FontSize == 14 && savedLayout["Left"].Bold, "view typography survives restart");
            Check(!savedLayout["Right"].Descending && savedLayout["Queue"].SortBy == "ProgressPercent", "view layouts remain independent");
            Check(new GlobalSettings().Layout is null, "old settings retain theme and default sorting");
            Check(!new GlobalSettings().EnableDownloadMeasurement, "download measurement disabled by default");
            using (var raceSlots = new RaceWatchSlots(1))
            {
                var firstSlot = raceSlots.EnterAsync(CancellationToken.None).GetAwaiter().GetResult();
                var waitingSlot = raceSlots.EnterAsync(CancellationToken.None);
                Check(!waitingSlot.IsCompleted, "new race watch queues when active capacity is full");
                firstSlot.Dispose(); firstSlot.Dispose();
                using var admitted = waitingSlot.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                using var cancelled = new CancellationTokenSource();
                var cancelledSlot = raceSlots.EnterAsync(cancelled.Token); cancelled.Cancel();
                var cancellationObserved=false;
                try { cancelledSlot.GetAwaiter().GetResult(); } catch(OperationCanceledException) { cancellationObserved=true; }
                Check(cancellationObserved, "waiting race watch can be cancelled without occupying a slot");
            }
            var skipOptions = new GlobalSettings(SkipPatterns: "*.nfo", SkipZeroByteFiles: true);
            Check(SkipRuleMatcher.ShouldSkip(skipOptions, "test.nfo"), "skip list enabled");
            Check(!SkipRuleMatcher.ShouldSkip(skipOptions with { EnableSkipList=false }, "test.nfo", size: 0), "disabled skip list bypasses zero and patterns");
            Check(!SkipRuleMatcher.ShouldSkip(skipOptions with { ApplySkipListToFolders=false }, "test.nfo", true), "folder matching can be disabled");
            Check(SkipRuleMatcher.ShouldSkip(skipOptions, "test.rar", size:0) && !SkipRuleMatcher.ShouldSkip(skipOptions, "test.rar", size:null), "only known zero-byte files skipped");
            Check(!SkipRuleMatcher.ShouldSkip(skipOptions with { SkipTransferMode="Auto" }, "test.nfo") && SkipRuleMatcher.ShouldSkip(skipOptions with { SkipTransferMode="Auto" }, "test.nfo", automatic:true), "automatic skip mode");
            Check(SkipRuleMatcher.ShouldSkip(skipOptions with { SkipTransferMode="Manual" }, "test.nfo") && !SkipRuleMatcher.ShouldSkip(skipOptions with { SkipTransferMode="Manual" }, "test.nfo", automatic:true), "manual skip mode");
            var skipPackage = new SkipListPackage("*.nfo", [new("(?i)^sample$", true, "Directory", "Deny", "Transfer")], Mode:"Auto");
            skipPackage.Validate();
            var packageReloaded=System.Text.Json.JsonSerializer.Deserialize<SkipListPackage>(System.Text.Json.JsonSerializer.Serialize(skipPackage))!;
            Check(packageReloaded.Mode=="Auto" && packageReloaded.Rules![0].Regex && packageReloaded.Patterns=="*.nfo", "skip list package round trip");
            var invalidRegexRejected=false;
            try { new SkipListPackage(Rules:[new("[", true)]).Validate(); } catch(ArgumentException) { invalidRegexRejected=true; }
            Check(invalidRegexRejected, "invalid imported regex rejected");
            Check(RaceSlotBudget.Transfers(4, 2, 2, 1) == 3, "RACE leaves one slot for shared listing and verification");
            Check(RaceSlotBudget.Transfers(4, 2, 2, 2) == 2, "RACE accounts for a browsing connection");
            Check(RaceSlotBudget.Transfers(2, 1, 1, 1) == 1, "lowered RACE total reduces pool capacity");
            Check(RaceSlotBudget.Transfers(1, 1, 1, 1) == 0, "no spare transfer slot does not overcommit");
            Check(RaceSlotBudget.Transfers(10, 1, 2, 1) == 3, "RACE directional limits cap prewarming");
            Check(FxpExistingFilePolicy.ShouldSkip(new(), 100, 50), "partial FXP target skipped by default");
            Check(FxpExistingFilePolicy.ShouldSkip(new(), 100, 100) && FxpExistingFilePolicy.ShouldSkip(new(), 100, 150), "same/larger FXP targets skipped by default");
            Check(!FxpExistingFilePolicy.ShouldSkip(new(), 100, null) && !FxpExistingFilePolicy.ShouldSkip(new(), null, 50), "unknown sizes do not skip");
            Check(!FxpExistingFilePolicy.ShouldSkip(new(FxpSkipSmallerTarget: false), 100, 50), "partial FXP target skip can be disabled");
            Check(FxpExistingFilePolicy.ShouldSkip(new(), 100, 0), "zero-byte existing FXP target skipped");
            Check(FxpExistingFilePolicy.ShouldSkip(new(), 100, 50, true) && FxpExistingFilePolicy.ShouldSkip(new(), 100, 100, true), "RACE now follows selected target policy");
            new GlobalSettingsStore(directory).Save(settings with { EnableDownloadMeasurement = true });
            Check(new GlobalSettingsStore(directory).Load().EnableDownloadMeasurement, "download measurement preference persists");
            var session = new SessionStatistics();
            System.Threading.Tasks.Parallel.For(0, 1000, _ =>
            {
                session.Complete("Download", 10); session.WorkerCreated(); session.PoolHit(); session.WorkerClosed();
            });
            session.Complete("Upload", 30); session.Complete("FXP", 40); session.Complete("Local", 50);
            Check(session.Downloaded == 10000 && session.Uploaded == 30 && session.Fxp == 40 && session.Local == 50 && session.Completed == 1003,
                "thread-safe independent session transfer counters");
            Check(session.Created == 1000 && session.Closed == 1000 && session.Hits == 1000, "session pool counters");
            Check(new SessionStatistics().Completed == 0 && new SessionStatistics().Downloaded == 0, "new session resets totals");
            Check(IrcStatisticsCommands.Execute("!flux stats", kind => kind) == "session", "IRC session stats route");
            Check(IrcStatisticsCommands.Execute("!FLUX STATS TOTAL", kind => kind) == "total", "IRC total stats route ignores case");
            Check(IrcStatisticsCommands.Execute("!flux stats pool", kind => kind) == "pool", "IRC pool stats route");
            var adminSite = new IoFtp.Core.Models.ConnectionProfile(Guid.NewGuid(), "Admin", "example.test", 21, "admin", IoFtp.Core.Models.TransferProtocol.FtpsExplicit);
            Check(IrcFtpAccess.ConfigurationError(adminSite with { AllowInvalidCertificate = true })!.Contains("invalid certificates"), "IRC certificate exception has specific diagnostic");
            Check(IrcFtpAccess.ConfigurationError(adminSite with { AllowInvalidCertificate = false }) is null, "verified FTPS authority accepted");
            Check(IrcFtpAccess.ConfigurationError(null)!.Contains("unavailable"), "missing IRC authority has specific diagnostic");
            Check(IrcStatisticsCommands.Execute("!flux stats reset", _ => throw new Exception("Invalid stats request reached snapshot")).StartsWith("ERROR:"), "IRC unknown stats option rejected");
            var totalDirectory = Path.Combine(directory, "totals");
            var totals = new TotalStatisticsStore(totalDirectory);
            totals.Complete("Download", 100); totals.Complete("Upload", 200); totals.Complete("FXP", 300); totals.Complete("Local", 400);
            var totalReloaded = new TotalStatisticsStore(totalDirectory).Snapshot();
            Check(totalReloaded is { Downloaded: 100, Uploaded: 200, Fxp: 300, Local: 400, Completed: 4 }, "all-time totals survive restart");
            new TotalStatisticsStore(totalDirectory).Complete("Download", 50);
            Check(new TotalStatisticsStore(totalDirectory).Snapshot().Downloaded == 150, "next session adds to existing totals");
            File.WriteAllText(Path.Combine(totalDirectory, "total-statistics.json"), "invalid json");
            var recoveredTotals = new TotalStatisticsStore(totalDirectory);
            Check(recoveredTotals.Snapshot().Completed == 4 && recoveredTotals.LastError is not null, "corrupt totals recover from backup with warning");
            new GlobalSettingsStore(directory).Save(settings with { UseAdvancedSkipFilters = false, SkipPatterns = "*.message", MetadataSkipDefaultsApplied = false });
            var skipSettings = new GlobalSettingsStore(directory).Load();
            Check(SkipRuleMatcher.ShouldSkip(skipSettings, "[ioNiNJA]-(2.7GB 20F-COMPLETE)-[ioNiNJA]", true, "Transfer"), "COMPLETE directory skipped");
            Check(SkipRuleMatcher.ShouldSkip(skipSettings, "[ioninja]-(850MB 5F-COMPLETE)-[ioninja]", true, "Transfer"), "COMPLETE size/count and case variations skipped");
            Check(SkipRuleMatcher.ShouldSkip(skipSettings, ".ioFTPD.verified", false, "Transfer"), "verification file skipped");
            Check(SkipRuleMatcher.ShouldSkip(skipSettings, "[ioNiNJA]-(V_MPEG4;ISO;AVC @ 7 647 Kbps. 1920x1080 16 9-E-AC-3 @ 224 Kbps)-[ioNiNJA]", true, "Transfer"), "video metadata with semicolons skipped");
            Check(SkipRuleMatcher.ShouldSkip(skipSettings, "[ioninja]-(V_MPEGH;ISO;HEVC @ 15000 Kbps. 3840x2160)-[ioninja]", true, "Transfer"), "video codec and resolution variations skipped");
            new GlobalSettingsStore(directory).Save(skipSettings with { SkipPatterns = "*.message", VideoMetadataSkipDefaultsApplied = false });
            var videoMigrated = new GlobalSettingsStore(directory).Load();
            Check(videoMigrated.SkipPatterns.Contains("[ioNiNJA]-(V_*)-[ioNiNJA]") && !videoMigrated.SkipPatterns.Contains(".ioFTPD.verified"), "video migration preserves previously removed metadata rules");
            Check(SkipRuleMatcher.ShouldSkip(skipSettings, "release.message", false, "Transfer"), "existing skip patterns retained");
            Check(!SkipRuleMatcher.ShouldSkip(skipSettings, "release.rar", false, "Transfer") &&
                !SkipRuleMatcher.ShouldSkip(skipSettings, "[ioNiNJA]-(20F-INCOMPLETE)-[ioNiNJA]", true, "Transfer"), "normal files and unrelated directories preserved");
            new GlobalSettingsStore(directory).Save(skipSettings with { SkipPatterns = "*.message" });
            Check(new GlobalSettingsStore(directory).Load().SkipPatterns == "*.message", "user can remove metadata rules after migration");
            Check(!new GlobalSettingsStore(directory).Load().EnableFxpSpeedMonitoring, "FXP monitoring disabled by default");
            new GlobalSettingsStore(directory).Save(settings with { EnableFxpSpeedMonitoring = false });
            Check(!new GlobalSettingsStore(directory).Load().EnableFxpSpeedMonitoring, "disabled FXP monitoring survives restart");
            new GlobalSettingsStore(directory).Save(settings);
            var reloaded = new GlobalSettingsStore(directory).Load().Irc!;
            Check(reloaded.Enabled && reloaded.Host == irc.Host && reloaded.Port == irc.Port && reloaded.Nick == irc.Nick && reloaded.Channel == irc.Channel, "connection survives restart without FTP authority");
            Check(reloaded.Password == irc.Password && reloaded.ZncUsername == irc.ZncUsername && reloaded.ZncNetwork == irc.ZncNetwork && reloaded.UseZnc, "ZNC settings survive restart");
            Check(reloaded.NetworkName == irc.NetworkName && reloaded.AccountLinks!.SequenceEqual(irc.AccountLinks!), "network and mappings persist");
            Check(new IrcSettings().AllowInvalidCertificate, "self-signed accepted by default");
            Check(!reloaded.AllowInvalidCertificate, "explicit strict certificate setting preserved");
            new GlobalSettingsStore(directory).Save(settings with { Irc = irc with { AllowInvalidCertificate = true } });
            Check(new GlobalSettingsStore(directory).Load().Irc!.AllowInvalidCertificate, "certificate exception survives restart");
            Check(!IrcService.AcceptCertificate(irc, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors), "self-signed rejected by default");
            Check(IrcService.AcceptCertificate(irc with { AllowInvalidCertificate = true }, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors), "self-signed accepted when selected");
            Check(IrcService.AcceptCertificate(irc, System.Net.Security.SslPolicyErrors.None), "valid certificate accepted");
            new GlobalSettingsStore(directory).Save(settings with { Irc = irc with { Enabled = false } });
            Check(new GlobalSettingsStore(directory).Load().Irc!.Host == irc.Host, "disabled IRC retains details");
            var blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "x");
            var failed = false;
            try { new GlobalSettingsStore(blocked).Save(settings); } catch (IOException) { failed = true; }
            Check(failed, "save failure reaches dialog");
            Console.WriteLine($"PASS: {count} global settings persistence checks.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
