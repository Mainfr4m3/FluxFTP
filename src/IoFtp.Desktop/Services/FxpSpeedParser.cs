using System.Globalization;
using System.Text.RegularExpressions;
namespace IoFtp.Desktop.Services;
internal static class FxpSpeedParser
{
    public static long ClampProgress(long bytes, long total) => Math.Max(0, total > 0 ? Math.Min(bytes, total) : bytes);
    public static bool IsPreallocatedSize(long bytes, long total) => total > 0 && bytes >= total;
    public static bool MatchesFile(string value, string name)
    {
        value = value.Replace('\\', '/').Trim();
        if (value.StartsWith("STOR ", StringComparison.OrdinalIgnoreCase) || value.StartsWith("RETR ", StringComparison.OrdinalIgnoreCase))
            value = value[5..].Trim();
        return value.Equals(name, StringComparison.OrdinalIgnoreCase) || value.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase);
    }
    public static bool TryReadIoFtpdTransfer(string response, string fileName, bool expectUpload,
        out long transferred, out long speed)
    {
        transferred = -1;
        speed = 0;

        var matches = 0;
        foreach (var line in response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var payload = line.Length > 4 && char.IsDigit(line[0]) && char.IsDigit(line[1]) && char.IsDigit(line[2])
                ? line[4..].Trim()
                : line.Trim();
            if (!payload.StartsWith("cid |", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = payload.Split('|').Select(part => part.Trim()).ToArray();
            if (parts.Length < 19) continue;
            var action = $"{parts[10]} {parts[16]}";
            var expectedAction = expectUpload
                ? action.Contains("STOR", StringComparison.OrdinalIgnoreCase) || action.Contains("UPLOAD", StringComparison.OrdinalIgnoreCase)
                : action.Contains("RETR", StringComparison.OrdinalIgnoreCase) || action.Contains("DOWNLOAD", StringComparison.OrdinalIgnoreCase);
            if (!expectedAction) continue;

            var bytes = long.TryParse(parts[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBytes)
                ? parsedBytes
                : -1;
            var parsedSpeed = ParseIoFtpdSpeed(parts[18]);
            if (MatchesFile(parts[10], fileName) || MatchesFile(parts[12], fileName) || MatchesFile(parts[13], fileName))
            {
                transferred = bytes;
                speed = parsedSpeed;
                matches++;
            }

        }
        return matches == 1;
    }

    public static long ParseIoFtpdSpeed(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var normalized = value.Trim().Replace(',', '.');
        var number = new string(normalized.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) return 0;
        var unit = normalized[number.Length..].Trim().ToLowerInvariant();
        var multiplier = unit.StartsWith("g") ? 1024d * 1024 * 1024
            : unit.StartsWith("m") ? 1024d * 1024
            : unit.StartsWith("b") ? 1d
            : 1024d; // ioFTPD TRANSFERSPEED without a suffix is KiB/s.
        return Math.Max(0, (long)(amount * multiplier));
    }

    public static bool TryReadDrFtpdTransfer(string response, string fileName, bool expectUpload, out long speed)
    {
        speed = 0;
        var direction = expectUpload ? "UP" : "DN";
        var matches = 0;
        foreach (var rawLine in response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = Regex.Replace(rawLine, @"^\s*\d{3}[- ]\s*", "").Trim();
            var match = Regex.Match(line,
                $@"->\s*{direction}\s+(?<speed>[0-9]+(?:[.,][0-9]+)?\s*[KMGTPE]?i?B)/s\s+(?:to|from)\s+.+?\s+-\s+(?<file>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var parsedSpeed = ParseDrFtpdSpeed(match.Groups["speed"].Value);
            var file = match.Groups["file"].Value.Trim();
            if (MatchesFile(file, fileName))
            {
                speed = parsedSpeed;
                matches++;
            }
        }
        return matches == 1;
    }

    public static long ParseDrFtpdSpeed(string value)
    {
        var match = Regex.Match(value.Trim().Replace(',', '.'),
            @"^(?<amount>[0-9]+(?:\.[0-9]+)?)\s*(?<prefix>[KMGTPE]?)(?<binary>I?)B$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !double.TryParse(match.Groups["amount"].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) return 0;
        var prefix = match.Groups["prefix"].Value;
        var exponent = prefix.Length == 0 ? 0 : "KMGTPE".IndexOf(prefix.ToUpperInvariant(), StringComparison.Ordinal) + 1;
        var basis = match.Groups["binary"].Value.Length > 0 ? 1024d : 1000d;
        var multiplier = exponent <= 0 ? 1d : Math.Pow(basis, exponent);
        return Math.Max(0, (long)(amount * multiplier));
    }

}
