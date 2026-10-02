using System.Text.RegularExpressions;
using IoFtp.Core.Abstractions;

namespace IoFtp.Core.Transport;

public static class TransferRejection
{
    public static string FormatDirectoryProbeReply(string? path, string message)
    {
        var plain = Clean(message);
        if (path is not null && plain.StartsWith("< 550 ", StringComparison.Ordinal) &&
            plain.EndsWith(": No such file or directory.", StringComparison.OrdinalIgnoreCase) && Category(plain) is null)
            return $"< Destination folder not found (directory check): {path}";
        return message;
    }

    public static string Clean(string message) => Regex.Replace(message, @"\x1B\[[0-9;?]*[ -/]*[@-~]", "");

    public static string? Category(string message)
    {
        var text = Clean(message);
        if (Regex.IsMatch(text, @"SAMPLE:\s*Only\s+1\s+sample\s+per\s+SAMPLE\s+DIR\b", RegexOptions.IgnoreCase)) return "SAMPLE";
        if (Regex.IsMatch(text, @"\[(?:NUKE|NUKED)\]|\bNUKE(?:D)?\s*(?:[:/]|detection\b|release\b)|\brelease\s+(?:is\s+)?nuked\b", RegexOptions.IgnoreCase)) return "NUKE";
        if (Regex.IsMatch(text, @"\[DupeCheck\]|\bDUPE\s*:|\bduplicate\s+(?:file|directory|release)\b", RegexOptions.IgnoreCase)) return "DUPE";
        return null;
    }

    public static string FormatFailure(string filename, string message)
    {
        var clean = Clean(message).Trim();
        var category = Category(clean);
        var heading = category is null ? "Transfer failed" : $"Transfer blocked [{category}]";
        // Keep the original FTP status and box on separate lines, including spacing.
        return $"{heading} ({filename}):{Environment.NewLine}{clean}";
    }

    public static async Task EnsureDirectoryAsync(string path,
        Func<string, CancellationToken, Task<RemoteCommandResult>> command, CancellationToken token)
    {
        var current = "";
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + part;
            var cwd = await command("CWD " + current, token);
            if (cwd.StatusCode is >= 200 and < 300) continue;
            var mkdir = await command("MKD " + current, token);
            if (mkdir.StatusCode is >= 200 and < 300) continue;
            // A parallel upload may have created the same directory in the meantime.
            cwd = await command("CWD " + current, token);
            if (cwd.StatusCode is >= 200 and < 300) continue;
            throw new IOException($"Cannot create destination directory {current}. MKD returned FTP {mkdir.StatusCode}: {Clean(mkdir.Message).Trim()}");
        }
    }
}
