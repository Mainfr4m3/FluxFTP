using System.IO;
using System.Text.RegularExpressions;
using IoFtp.Core.Transport;

namespace IoFtp.Desktop.Services;

internal sealed record LockedDirectorySkip(string Site, bool Destination)
{
    private static bool IsMoveSite(string? site) => site is not null &&
        (site.Equals("GBIT01", StringComparison.OrdinalIgnoreCase) || site.Equals("GBIT02", StringComparison.OrdinalIgnoreCase));

    public static LockedDirectorySkip? Match(Exception error, string? source, string? destination)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is RaceDirectorySkippedException skipped) return skipped.Skip;
            var directory = current as DestinationDirectoryException;
            var ftp = current as FtpCommandException;
            if (directory is null && !(ftp?.StatusCode == 550 && ftp.Command?.StartsWith("CWD ", StringComparison.OrdinalIgnoreCase) == true)) continue;
            if (directory is not null && !directory.IsPermanent) continue;
            if (!Regex.IsMatch(current.Message, @"\b(?:permission|access) denied\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
            var site = directory?.Site ?? ftp?.Site ?? (directory is not null ? destination : null);
            if (!IsMoveSite(site)) continue;
            if (string.Equals(site, destination, StringComparison.OrdinalIgnoreCase)) return new(site!, true);
            if (string.Equals(site, source, StringComparison.OrdinalIgnoreCase)) return new(site!, false);
        }
        return null;
    }
}

internal sealed class RaceDirectorySkippedException(LockedDirectorySkip skip)
    : IOException($"SKIP [{skip.Site}]: directory locked (550); no retry for this race route")
{
    public LockedDirectorySkip Skip { get; } = skip;
}
