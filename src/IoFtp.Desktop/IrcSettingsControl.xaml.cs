using System.Windows.Controls;
using IoFtp.Core.Models;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;
namespace IoFtp.Desktop;
public partial class IrcSettingsControl : UserControl
{
    public IrcSettingsControl() { InitializeComponent(); }
    internal void Load(IrcSettings irc)
    {
        IrcEnabledBox.IsChecked = irc.Enabled; IrcHostBox.Text = irc.Host; IrcPortBox.Text = irc.Port.ToString();
        IrcTlsBox.IsChecked = irc.UseTls; IrcNickBox.Text = irc.Nick; IrcPasswordBox.Password = irc.Password;
        IrcNetworkNameBox.Text = irc.NetworkName;
        IrcAllowInvalidCertificateBox.IsChecked = irc.AllowInvalidCertificate;
        IrcZncBox.IsChecked = irc.UseZnc; IrcZncUserBox.Text = irc.ZncUsername; IrcZncNetworkBox.Text = irc.ZncNetwork;
        IrcChannelBox.Text = irc.Channel; IrcSiteBox.ItemsSource = new ProfileStore().Load(); IrcSiteBox.SelectedValue = irc.AdminSiteId;
        IrcLoginBox.IsChecked = irc.AllowPrivateLogin; IrcNewsPathBox.Text = irc.NewsPath;
        IrcLinksBox.Text = string.Join(Environment.NewLine, (irc.AccountLinks ?? []).Select(link => $"{link.Account}={link.FtpUser}"));
    }

    internal IrcSettings? Read()
    {
        ErrorText.Text = "";
        var links = new List<IrcAccountLink>();
        foreach (var line in IrcLinksBox.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !IrcProtocol.Account(pair[0]) || !IrcProtocol.FtpUser(pair[1]) || links.Any(link => link.Account.Equals(pair[0], StringComparison.OrdinalIgnoreCase)))
            { ErrorText.Text = "IRC links must be unique IRC-account=FTP-user pairs."; return null; }
            links.Add(new(pair[0], pair[1]));
        }
        if (!int.TryParse(IrcPortBox.Text, out var port) || port is < 1 or > 65535)
        { ErrorText.Text = "IRC port must be between 1 and 65535."; return null; }
        var irc = new IrcSettings(IrcEnabledBox.IsChecked == true, IrcHostBox.Text.Trim(), port, IrcTlsBox.IsChecked == true,
            IrcNickBox.Text.Trim(), IrcPasswordBox.Password, IrcZncBox.IsChecked == true, IrcZncUserBox.Text.Trim(),
            IrcZncNetworkBox.Text.Trim(), IrcChannelBox.Text.Trim(), (IrcSiteBox.SelectedItem as ConnectionProfile)?.Id,
            IrcLoginBox.IsChecked == true, IrcNewsPathBox.Text.Trim(), links.ToArray(), IrcNetworkNameBox.Text.Trim(),
            IrcAllowInvalidCertificateBox.IsChecked == true);
        if (!IrcProtocol.Token(irc.NetworkName) || irc.NetworkName.Length > 32)
        { ErrorText.Text = "Enter a network name of 1–32 characters without spaces."; return null; }
        if (irc.Password.Any(char.IsControl) || System.Text.Encoding.UTF8.GetByteCount(irc.Password) > 250)
        { ErrorText.Text = "IRC password is too long or contains control characters."; return null; }
        if (!irc.Enabled) return irc;
        if (string.IsNullOrWhiteSpace(irc.Host) || irc.Host.Any(char.IsWhiteSpace) || irc.Host.Any(char.IsControl) ||
            !System.Text.RegularExpressions.Regex.IsMatch(irc.Nick, @"\A[A-Za-z_\[\]{}|`^][A-Za-z0-9_\[\]{}|`^\-]{0,29}\z") ||
            !IrcProtocol.Token(irc.Channel) || !irc.Channel.StartsWith('#'))
        { ErrorText.Text = "Enter an IRC host, valid nickname and one #channel."; return null; }
        if (irc.UseZnc && (!IrcProtocol.Token(irc.ZncUsername) || !IrcProtocol.Token(irc.ZncNetwork) || irc.ZncUsername.Contains('/') || irc.ZncNetwork.Contains('/') || irc.Password.Length == 0))
        { ErrorText.Text = "Enter a ZNC username, network and password."; return null; }
        // Saving IRC connection details does not require an FTP authority site.
        // IrcService still requires TLS for password login and verified FTPS for admin commands.
        return irc;
    }


}
