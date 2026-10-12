using System.Text.RegularExpressions;
using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal sealed record SkipListPackage(string Patterns = "", AdvancedSkipRule[]? Rules = null,
    bool Enabled = true, bool ZeroByteFiles = false, bool EmptyDirectories = false,
    bool ApplyToFolders = true, string Mode = "Both", string Color = "#7F9FBF")
{
    public void Validate()
    {
        if (Mode is not ("Both" or "Auto" or "Manual")) throw new ArgumentException("Invalid skip mode.");
        foreach (var rule in Rules ?? [])
        {
            if (rule.EntryType is not ("File" or "Directory" or "Both") || rule.Action is not ("Allow" or "Deny"))
                throw new ArgumentException("Invalid rule type or action.");
            if (rule.Regex) _ = new Regex(rule.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        }
    }
}
