namespace IoFtp.Desktop.Services;

internal static class VisionaryLogVisibility
{
    public static bool IsCommandReport(string message) =>
        message.TrimStart().StartsWith("SERVER STATS", StringComparison.OrdinalIgnoreCase) ||
        message.TrimStart().StartsWith("SERVER DETAILS", StringComparison.OrdinalIgnoreCase) &&
        message.Contains("SITE ", StringComparison.OrdinalIgnoreCase);

    public static string? FailureSite(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"\bsite=([^;|\r\n]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var site = match.Success ? match.Groups[1].Value.Trim() : null;
        return string.IsNullOrWhiteSpace(site) || site.Equals("unknown", StringComparison.OrdinalIgnoreCase) ? null : site;
    }

    public static string FailureContext(string message, string? source, string? destination)
    {
        if (FailureSite(message) is { } site) return site;
        if (string.IsNullOrWhiteSpace(source)) source = null;
        if (string.IsNullOrWhiteSpace(destination)) destination = null;
        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(destination))
            return $"route {source} → {destination}";
        return source ?? destination ?? "FTP";
    }

    public static bool Is550(string message) => System.Text.RegularExpressions.Regex.IsMatch(message,
        @"(?:^|[\s:])550(?:[\s:-]|$)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string CompactTransferFailure(string filename, string message, string? site = null)
    {
        var category = IoFtp.Core.Transport.TransferRejection.Category(message);
        var heading = category == "FILE-LOCKED" ? "FILE LOCK FAILED" : "TRANSFER FAILED";
        if (category != "FILE-LOCKED" && Is550(message)) heading += " (550)";
        else if (category is not null && category != "FILE-LOCKED") heading += " [" + category + "]";
        if (!string.IsNullOrWhiteSpace(site)) heading += " [" + site + "]";
        var result = filename == "FTP" ? heading : heading + " : " + filename;
        var flags = System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        var reason = System.Text.RegularExpressions.Regex.IsMatch(message, @"\b(?:permission|access) denied\b", flags) ? "ACCESS DENIED"
            : System.Text.RegularExpressions.Regex.IsMatch(message, @"\bno such file or directory\b|\bfile not found\b", flags) ? "NOT FOUND" : null;
        if (reason is not null)
        {
            var command = System.Text.RegularExpressions.Regex.Match(message, @"\bcommand=([A-Z]+)(?:\s|[;|)]|$)", flags);
            result += " — " + (command.Success ? command.Groups[1].Value.ToUpperInvariant() + ": " : "") + reason;
        }
        return result;
    }

    public static bool IsBridgeMessage(string message)
    {
        var text = message.TrimStart();
        return new[] { "VISIONARY", "BRIDGE", "RACE slots for ", "RACE prequeue:", "RACE watch [", "RACE RESULT [" }
            .Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static string? UploadBusySummary(string message)
    {
        var flags = System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (!System.Text.RegularExpressions.Regex.IsMatch(message, @"\b553\b", flags)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(message, @"\bFile\s+is\s+being\s+uploaded\s+by\s+([^\s;|()]+)", flags);
        return match.Success ? "UPLOADED by " + match.Groups[1].Value.TrimEnd('.', ',') : null;
    }

    public static (string Site, string Text)? LoginFailureSummary(string message)
    {
        // 530 on USER/PASS/ACCT rejects login; other 530 replies can be path permissions.
        var flags = System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (!System.Text.RegularExpressions.Regex.IsMatch(message, @"\b530\b", flags)) return null;
        var command = System.Text.RegularExpressions.Regex.Match(message, @"\bcommand=(USER|PASS|ACCT)\b", flags);
        if (!command.Success) return null;
        var site = System.Text.RegularExpressions.Regex.Match(message, @"\bsite=([^;\s|]+)", flags);
        if (!site.Success) return null;
        var name = site.Groups[1].Value;
        return (name, $"LOGIN FAILED [{name}] — 530 ACCESS DENIED ({command.Groups[1].Value.ToUpperInvariant()}); connection BLOCKED. Details: Bridge-Info.");
    }

    public static bool Show(string message, bool debug)
    {
        var text = message.TrimStart();
        if (text.StartsWith("SERVER STATS", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("SERVER DETAILS", StringComparison.OrdinalIgnoreCase)) return debug;
        if (new[] { "Skiplist", "Skip list:", "Existing target skipped:", "Smart resume", "FXP diagnostic", "RACE prequeue:", "RACE watch [", "VISIONARY parallel source " }
            .Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return debug;
        if (debug || !text.StartsWith("VISIONARY", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("VISIONARY TIMING", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.StartsWith("VISIONARY queued ", StringComparison.OrdinalIgnoreCase)) return true;
        return new[] { "error", "failed", "failure", "rejected", "blocked", "warning", "stopped", "timeout", "timed out", "NUKE" }
            .Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
