using IoFtp.Desktop.Models;
using System.Text.RegularExpressions;

namespace IoFtp.Desktop.Services;

internal sealed record IrcConnectRequest(string Host, int Port, string Nick, string Channel,
    bool UseTls = true, bool UseZnc = false, string ZncUsername = "", string ZncNetwork = "",
    string Password = "", string NetworkName = "Default", bool AllowInvalidCertificate = true)
{
    public IrcSettings Apply(IrcSettings? previous)
    {
        if (string.IsNullOrWhiteSpace(Host) || Uri.CheckHostName(Host) == UriHostNameType.Unknown || Port is < 1 or > 65535)
            throw new ArgumentException("Valid host and port are required.");
        if (Nick is null || !Regex.IsMatch(Nick, @"\A[A-Za-z_\[\]{}|`^][A-Za-z0-9_\[\]{}|`^\-]{0,29}\z") ||
            Channel is null || !IrcProtocol.Token(Channel) || !Channel.StartsWith('#') || Channel.Length > 64)
            throw new ArgumentException("Valid nick and one #channel are required.");
        if (NetworkName is null || !IrcProtocol.Token(NetworkName) || NetworkName.Length > 32 ||
            Password is null || Password.Any(char.IsControl) || System.Text.Encoding.UTF8.GetByteCount(Password) > 250)
            throw new ArgumentException("Invalid network name or password format.");
        if (UseZnc && (ZncUsername is null || ZncNetwork is null || !IrcProtocol.Token(ZncUsername) || !IrcProtocol.Token(ZncNetwork) ||
            ZncUsername.Contains('/') || ZncNetwork.Contains('/') || Password.Length == 0))
            throw new ArgumentException("ZNC username, network and password are required.");
        var current = previous ?? new();
        var sameIdentity = current.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) && current.Port == Port &&
            current.UseZnc == UseZnc && current.ZncUsername == ZncUsername && current.ZncNetwork == ZncNetwork;
        return current with { Enabled = true, Host = Host, Port = Port, Nick = Nick, Channel = Channel,
            UseTls = UseTls, UseZnc = UseZnc, ZncUsername = ZncUsername, ZncNetwork = ZncNetwork,
            Password = Password, NetworkName = NetworkName, AllowInvalidCertificate = AllowInvalidCertificate,
            AccountLinks = sameIdentity ? current.AccountLinks : [] };
    }
}
