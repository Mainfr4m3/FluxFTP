using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;
internal static class SkipMatchChecks
{
    public static void Run()
    {
        var s = new GlobalSettings(EnableSkipList: true, SkipZeroByteFiles: true,
            SkipPatterns: "*.message\n*.ioftpd\n*.ioFTPD.verified",
            AdvancedSkipRules: [new("^.*\\.(?:dctmp|bad)$", true, "File", "Deny", "Transfer"), new("^\\([^)]*COMPLETE[^)]*\\)$", true, "Directory", "Deny", "Transfer")]);
        if (SkipRuleMatcher.FindMatch(s, "archive.r16", scope: "Transfer", automatic: true, size: 36438016)?.Denied == true) throw new Exception("Enabling skiplist skipped a positive ordinary file");
        var zero = SkipRuleMatcher.FindMatch(s, "archive.sfv", scope: "Transfer", automatic: true, size: 0);
        if (zero?.Source != "Skip Zero Byte File" || zero.Denied != true) throw new Exception("Zero rule reason");
        if (SkipRuleMatcher.ShouldSkip(s, "archive.sfv", scope: "Transfer", automatic: true, size: null)) throw new Exception("Unknown size treated as zero");
        if (SkipRuleMatcher.FindMatch(s, "archive.bad", scope: "Transfer", automatic: true, size: 100)?.Source != "Global regex") throw new Exception("Regex reason");
        if (SkipRuleMatcher.FindMatch(s with { UseAdvancedSkipFilters = false }, ".ioFTPD.verified", scope: "Transfer", automatic: true, size: 100)?.Source != "Global wildcard list") throw new Exception("Wildcard reason");
        if (SkipRuleMatcher.FindMatch(s, "(COMPLETE)", false, "Transfer", true, 100)?.Denied == true || SkipRuleMatcher.FindMatch(s, "(COMPLETE)", true, "Transfer", true)?.Denied != true) throw new Exception("Directory-only rule");
        var broad = s with { AdvancedSkipRules = [new(".*", true, "File", "Deny", "Transfer")] };
        if (SkipRuleMatcher.FindMatch(broad, "archive.rar", scope: "Transfer", automatic: true, size: 100)?.Detail.Contains("pattern=.*") != true) throw new Exception("Catch-all rule diagnosis");
        if (SkipRuleMatcher.ShouldSkip(s with { EnableSkipList = false }, "archive.bad", scope: "Transfer", automatic: true, size: 100)) throw new Exception("Disabled list active");
        var current = new GlobalSettingsStore().Load();
        foreach (var name in new[] { "miss.moxy.2025.dutch.1080p.web.h264-tripel.r16", "watch.what.happens.live.2026.10.13.1080p.web.h264-edith.sfv", "love.of.your.life.2026.polish.2160p.web.h265-chopin.r08" })
        {
            var match = SkipRuleMatcher.FindMatch(current, name, scope: "Transfer", automatic: true, size: 36438016);
            Console.WriteLine($"Local rule check: {name} => {(match?.Denied == true ? match.Source + "; " + match.Detail : "allowed")}");
        }
        GlobalSettingsChecks.Run();
        Console.WriteLine("PASS: enabled list allows ordinary nonzero files, zero/unknown sizes, regex/wildcard reasons, entry types, catch-all diagnosis and logging persistence.");
    }
}
