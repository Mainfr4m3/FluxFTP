using System.Globalization;
using System.Text.RegularExpressions;
using IoFtp.Core.Abstractions;

namespace IoFtp.Desktop.Services;

internal sealed record IrcMessage(string Prefix, string Command, string[] Parameters, Dictionary<string, string> Tags)
{
    public string Nick => Prefix.Split('!')[0];
    public static IrcMessage Parse(string line)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        if (line.StartsWith('@'))
        {
            var end = line.IndexOf(' ');
            if (end < 0) return new("", "", [], tags);
            foreach (var tag in line[1..end].Split(';'))
            {
                var pair = tag.Split('=', 2);
                tags[pair[0]] = pair.Length == 2 ? pair[1] : "";
            }
            line = line[(end + 1)..];
        }
        var prefix = "";
        if (line.StartsWith(':'))
        {
            var end = line.IndexOf(' ');
            if (end < 0) return new("", "", [], tags);
            prefix = line[1..end]; line = line[(end + 1)..];
        }
        var trailing = line.IndexOf(" :", StringComparison.Ordinal);
        var words = (trailing >= 0 ? line[..trailing] : line).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count == 0) return new(prefix, "", [], tags);
        var command = words[0].ToUpperInvariant(); words.RemoveAt(0);
        if (trailing >= 0) words.Add(line[(trailing + 2)..]);
        return new(prefix, command, words.ToArray(), tags);
    }
}

internal static class IrcProtocol
{
    public static bool Token(string value) => value.Length is > 0 and <= 128 && value.All(c => c > ' ' && c < 127 && c is not ':' and not ',' and not ';');
    public static bool FtpUser(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9_.-]{1,64}\z");
    public static bool Account(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9_\[\]{}|`^.-]{1,64}\z");
    public static bool Same(string a, string b) => Fold(a) == Fold(b);
    private static string Fold(string value) => value.ToLowerInvariant().Replace('[', '{').Replace(']', '}').Replace('\\', '|').Replace('^', '~');
    public static bool IsLive(IrcMessage message, DateTimeOffset connectedAt, bool requireTime)
    {
        // Ignore all batched traffic: ZNC and IRC history must never replay commands.
        if (message.Tags.ContainsKey("batch")) return false;
        if (!message.Tags.TryGetValue("time", out var time)) return !requireTime;
        return DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var stamp)
            && stamp >= connectedAt && stamp <= DateTimeOffset.UtcNow.AddMinutes(1);
    }
    public static bool IsAdmin(RemoteCommandResult result)
    {
        if (result.StatusCode is < 200 or >= 300) return false;
        // Accept a labelled ioFTPD flags field, never numbers or flag letters elsewhere in a reply.
        var clean = Regex.Replace(result.Message, @"\x1b\[[0-9;]*m", "");
        var matches = Regex.Matches(clean, @"(?im)^\s*(?:\d{3}[- ])?\s*Flags\s*:\s*([A-Za-z0-9]+)\s*$");
        return matches.Count == 1 && matches[0].Groups[1].Value.Any(c => c is '1' or 'M');
    }
}
