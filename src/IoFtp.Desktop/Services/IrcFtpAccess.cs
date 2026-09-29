using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

internal static class IrcFtpAccess
{
    public static async Task<bool> VerifyAsync(ConnectionProfile site, string user, string? password,
        Func<IRemoteSession> createSession, CancellationToken token)
    {
        if (site.Protocol is not (TransferProtocol.FtpsExplicit or TransferProtocol.FtpsImplicit) || site.AllowInvalidCertificate ||
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
