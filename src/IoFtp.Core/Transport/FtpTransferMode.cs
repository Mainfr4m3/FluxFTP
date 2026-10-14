using System.Text.RegularExpressions;
using IoFtp.Core.Models;
namespace IoFtp.Core.Transport;
public static class FtpTransferMode
{
    public const string DefaultAsciiRules = "*.bat\n*.c\n*.cfm\n*.cgi\n*.cpp\n*.css\n*.dhtml\n*.h\n*.htm\n*.html\n*.ini\n*.mak\n*.pas\n*.pl\n*.shtm\n*.shtml\n*.td\n*.txt\n*.xml\n*.asp";
    public static void Validate(string rules) { foreach (var rule in Lines(rules)) _ = Pattern(rule); }
    private static IEnumerable<string> Lines(string rules) => rules.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static Regex Pattern(string rule) => new(rule.StartsWith("regex:", StringComparison.OrdinalIgnoreCase) ? rule[6..] : "^" + Regex.Escape(rule).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static bool IsAscii(string path, SiteOptions options) => !options.ForceBinaryMode && Lines(options.AsciiRules).Any(rule => Pattern(rule).IsMatch(path.Replace('\\', '/').Split('/')[^1]));
    public static bool CanResume(string path, SiteOptions options) => !options.NoAsciiResume || !IsAscii(path, options);
}
public sealed class FtpResumeRejectedException(string message, int statusCode = 0) : IOException(message)
{
    public int StatusCode { get; } = statusCode;
    public static bool IsAppendPermissionRejection(int code, string message) => code == 553 &&
        message.Contains("file permissions incorrect for resuming/appending", StringComparison.OrdinalIgnoreCase);
    public bool CanOverwrite => ((StatusCode is 500 or 501 or 502 or 504 or 550 or 551) || IsAppendPermissionRejection(StatusCode, Message)) && TransferRejection.Category(Message) is null;
}
