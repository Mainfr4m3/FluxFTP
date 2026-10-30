using System.Text.RegularExpressions;
using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal static class SkipRuleMatcher
{
    public static bool UsesAdvanced(GlobalSettings settings) => settings.EnableVisionaryBridge
        ? settings.UseAdvancedSkipFilters == true // Explicit opt-in; do not activate legacy imported rules implicitly.
        : settings.UseAdvancedSkipFilters ?? (settings.AdvancedSkipRules?.Any(rule => !string.IsNullOrWhiteSpace(rule.Pattern)) == true);

    public static bool Applies(GlobalSettings settings, bool automatic) => settings.EnableSkipList &&
        (settings.SkipTransferMode == "Both" || settings.SkipTransferMode == (automatic ? "Auto" : "Manual"));

    public static bool ShouldSkip(GlobalSettings settings, string name, bool isDirectory = false, string scope = "Allround", bool automatic = false, long? size = null) =>
        FindMatch(settings, name, isDirectory, scope, automatic, size)?.Denied == true;

    public static SkipRuleMatch? FindMatch(GlobalSettings settings, string name, bool isDirectory = false, string scope = "Allround", bool automatic = false, long? size = null)
    {
        if (!Applies(settings, automatic)) return null;
        if (!isDirectory && settings.SkipZeroByteFiles && size == 0) return new(true, "Skip Zero Byte File", "reported size=0");
        if (isDirectory && !settings.ApplySkipListToFolders) return null;
        if (UsesAdvanced(settings))
        {
        foreach (var rule in settings.AdvancedSkipRules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern) || !TypeMatches(rule.EntryType, isDirectory) || !ScopeMatches(rule.Scope, scope))
                continue;
            bool matches;
            try
            {
                matches = rule.Regex
                    ? Regex.IsMatch(name, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                    : System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(rule.Pattern, name, true);
            }
            catch (ArgumentException) { continue; }
            catch (RegexMatchTimeoutException) { continue; }
            if (matches) return new(rule.Action.Equals("Deny", StringComparison.OrdinalIgnoreCase), rule.Regex ? "Global regex" : "Global wildcard", $"pattern={rule.Pattern}; action={rule.Action}; type={rule.EntryType}; scope={rule.Scope}");
        }

        return null; // Advanced mode never falls through to the normal wildcard list.
        }

        var patterns = settings.SkipPatterns.Split(['\r', '\n', ',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pattern = patterns.FirstOrDefault(pattern => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, true));
        return pattern is null ? null : new(true, "Global wildcard list", "pattern=" + pattern);
    }

    private static bool TypeMatches(string type, bool isDirectory) =>
        type.Equals("Both", StringComparison.OrdinalIgnoreCase) ||
        isDirectory && type.Equals("Directory", StringComparison.OrdinalIgnoreCase) ||
        !isDirectory && type.Equals("File", StringComparison.OrdinalIgnoreCase);

    private static bool ScopeMatches(string configured, string requested) =>
        string.IsNullOrWhiteSpace(configured) ||
        configured.Equals("Allround", StringComparison.OrdinalIgnoreCase) ||
        configured.Equals(requested, StringComparison.OrdinalIgnoreCase);
}

internal sealed record SkipRuleMatch(bool Denied, string Source, string Detail);
