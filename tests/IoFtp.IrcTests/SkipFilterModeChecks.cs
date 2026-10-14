using System.Text.Json;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;
internal static class SkipFilterModeChecks
{
    public static void Run()
    {
        var s = new GlobalSettings(SkipPatterns: "*", AdvancedSkipRules: [new("\\.bad$", true, "File", "Deny", "Transfer")], UseAdvancedSkipFilters: true);
        bool Denied(GlobalSettings settings, string name) => SkipRuleMatcher.ShouldSkip(settings, name, scope: "Transfer", automatic: true, size: 100);
        if (Denied(s, "archive.rar") || !Denied(s, "archive.bad")) throw new Exception("Advanced filters fell through to normal wildcard list");
        if (!Denied(s with { UseAdvancedSkipFilters = false }, "archive.rar")) throw new Exception("Normal wildcard not restored");
        var normal = s with { SkipPatterns = "*.tmp", AdvancedSkipRules = [new(".*", true, "File", "Deny", "Transfer")], UseAdvancedSkipFilters = false };
        if (Denied(normal, "archive.rar") || !Denied(normal, "archive.tmp")) throw new Exception("Inactive advanced regex affected normal mode");
        if (Denied(s with { EnableSkipList = false }, "archive.bad")) throw new Exception("Master off ignored");
        if (!SkipRuleMatcher.UsesAdvanced(s with { UseAdvancedSkipFilters = null })) throw new Exception("Old advanced rules not migrated");
        if (SkipRuleMatcher.UsesAdvanced(new())) throw new Exception("Fresh config did not use normal mode");
        var package = new SkipListPackage("*", s.AdvancedSkipRules, UseAdvancedFilters: true); package.Validate();
        if (JsonSerializer.Deserialize<SkipListPackage>(JsonSerializer.Serialize(package))?.UseAdvancedFilters != true) throw new Exception("Export/import mode lost");
        foreach (var advanced in new[] { false, true })
        {
            var bridge = s with { EnableVisionaryBridge = true, UseAdvancedSkipFilters = advanced, SkipZeroByteFiles = true };
            if (SkipRuleMatcher.UsesAdvanced(bridge)) throw new Exception("Global advanced filters active with Visionary");
            if (!Denied(bridge, "archive.rar")) throw new Exception("Optional normal wildcard list inactive with Visionary");
            if (Denied(bridge with { SkipPatterns = "*.tmp" }, "archive.bad")) throw new Exception("Advanced regex active with Visionary");
            if (Denied(bridge with { EnableSkipList = false }, "archive.rar")) throw new Exception("Normal skiplist cannot be disabled with Visionary");
        }
        GlobalSettingsChecks.Run(); SkipMatchChecks.Run();
        Console.WriteLine("PASS: mutually exclusive wildcard/advanced modes, regex-only fallback exclusion, master off, old settings inference and package persistence.");
    }
}
