using System.Text.RegularExpressions;
using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal static class SkipRuleMatcher
{
    public static bool Applies(GlobalSettings settings, bool automatic) => settings.EnableSkipList &&
        (settings.SkipTransferMode == "Both" || settings.SkipTransferMode == (automatic ? "Auto" : "Manual"));

    public static bool ShouldSkip(GlobalSettings settings, string name, bool isDirectory = false, string scope = "Allround", bool automatic = false, long? size = null)
    {
        if (!Applies(settings, automatic)) return false;
        if (!isDirectory && settings.SkipZeroByteFiles && size == 0) return true;
        if (isDirectory && !settings.ApplySkipListToFolders) return false;
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
            if (matches) return rule.Action.Equals("Deny", StringComparison.OrdinalIgnoreCase);
        }

        var patterns = settings.SkipPatterns.Split(['\r', '\n', ',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return patterns.Any(pattern => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, true));
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
