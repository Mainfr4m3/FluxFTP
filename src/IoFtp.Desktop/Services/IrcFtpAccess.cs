using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

internal static class IrcFtpAccess
{
    public static string? ConfigurationError(ConnectionProfile? site)
    {
        if (site is null) return "ERROR: IRC admin site is unavailable. Select an existing admin site in IRC settings.";
        if (site.Protocol is not (TransferProtocol.FtpsExplicit or TransferProtocol.FtpsImplicit))
            return "ERROR: IRC admin verification requires an FTPS admin site.";
        if (site.AllowInvalidCertificate)
            return "ERROR: IRC admin verification is blocked because the admin site allows invalid certificates. Configure a trusted server certificate and disable Allow invalid certificate in that site's Connection details.";
        return null;
    }
    public static async Task<bool> VerifyAsync(ConnectionProfile site, string user, string? password,
        Func<IRemoteSession> createSession, CancellationToken token)
    {
        if (ConfigurationError(site) is not null ||
            !IrcProtocol.FtpUser(user) || password?.Any(char.IsControl) == true) return false;
        if (password is not null)
        {
            await using var login = createSession();
            await login.ConnectAsync(site with { Username = user, Password = password }, token);
        }
        await using var lookup = createSession();
        await lookup.ConnectAsync(site, token);
        return IrcProtocol.IsAdmin(await lookup.ExecuteCommandAsync($"SITE USER {user}", token));
    }
}
