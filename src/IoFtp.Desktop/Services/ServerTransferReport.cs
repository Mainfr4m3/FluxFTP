using System.Text.RegularExpressions;

namespace IoFtp.Desktop.Services;

internal static class ServerTransferReport
{
    public static string Clean(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return "";
        // Bound cached reports and remove terminal/control formatting, retaining the server's table layout.
        var text = response[..Math.Min(response.Length, 65_536)];
        text = Regex.Replace(text, @"\x1B\[[0-9;?]*[ -/]*[@-~]", "");
        text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
        return Regex.Replace(text, @"(?m)^\d{3}[- ]", "").Trim();
    }
    public static string? Statistics(string response)
    {
        var text = Clean(response);
        return Regex.IsMatch(text, @"\b(?:UL|DL|Credits|Ratio|Speed|User[- ]?Top|Group[- ]?Top)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? text : null;
    }
}
