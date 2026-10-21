using System.Globalization;
using System.Text.RegularExpressions;

namespace IoFtp.Desktop.Services;

internal static class FtpLogTimestamp
{
    public static string Format(string message, DateTime now)
    {
        var stamp = now.ToString("yyyy.MM.dd-HH:mm:ss", CultureInfo.InvariantCulture);
        return string.Join("\n", message.Split('\n').Select(line =>
        {
            if (string.IsNullOrWhiteSpace(line)) return line;
            if (Regex.IsMatch(line, @"^\d{4}\.\d{2}\.\d{2}-\d{2}:\d{2}:\d{2} ")) return line;
            // Replace the older time-only prefix, retaining site and reply text.
            line = Regex.Replace(line, @"^\d{2}:\d{2}:\d{2}\s+", "");
            return stamp + " " + line;
        }));
    }
}
