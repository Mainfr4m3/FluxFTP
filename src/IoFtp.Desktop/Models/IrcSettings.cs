namespace IoFtp.Desktop.Models;

public sealed record IrcAccountLink(string Account, string FtpUser);

public sealed record IrcSettings(
    bool Enabled = false,
    string Host = "",
    int Port = 6697,
    bool UseTls = true,
    string Nick = "FluxFTP",
    string Password = "",
    bool UseZnc = false,
    string ZncUsername = "",
    string ZncNetwork = "",
    string Channel = "",
    Guid? AdminSiteId = null,
    bool AllowPrivateLogin = true,
    string NewsPath = "/",
    IrcAccountLink[]? AccountLinks = null,
    string NetworkName = "Default",
    bool AllowInvalidCertificate = true);
