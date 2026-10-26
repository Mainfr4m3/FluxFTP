using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using IoFtp.Core.Models;
namespace IoFtp.Linux;
internal sealed class SitesWindow : Window
{
    public SitesWindow(LinuxConfiguration configuration, Action changed, Func<Guid, bool, Task> connect)
    {
        Title = "Sites"; Width = 980; Height = 720; MinWidth = 720; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), Margin = new Thickness(16) };
        var names = new ListBox { ItemTemplate = new FuncDataTemplate<SavedSite>((site, _) => new TextBlock { Text = site?.Profile.Name }) };
        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var add = new Button { Content = "New site", Padding = new Thickness(10, 6) };
        DockPanel.SetDock(add, Dock.Bottom); left.Children.Add(add); left.Children.Add(names); root.Children.Add(left);
        var right = new DockPanel(); Grid.SetColumn(right, 1); root.Children.Add(right);
        var report = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FFB179"), Margin = new Thickness(0, 8) };
        DockPanel.SetDock(report, Dock.Bottom); right.Children.Add(report);
        var holder = new ContentControl(); right.Children.Add(holder);
        void Reload(Guid? id = null)
        {
            var sites = configuration.Load().Sites.OrderBy(site => site.Profile.Name, StringComparer.OrdinalIgnoreCase).ToList();
            names.ItemsSource = sites; names.SelectedItem = sites.FirstOrDefault(site => site.Profile.Id == id);
        }
        void Edit(SavedSite? saved)
        {
            var profile = saved?.Restore() ?? new ConnectionProfile(Guid.NewGuid(), "", "", 21, "", TransferProtocol.FtpsExplicit);
            var options = profile.EffectiveOptions;
            var basic = SettingsUi.Form(); basic.Margin = new Thickness(8);
            var name = SettingsUi.Field(basic, "Site name", profile.Name);
            var host = SettingsUi.Field(basic, "Host", profile.Host);
            var port = SettingsUi.Field(basic, "Port", profile.Port.ToString());
            var user = SettingsUi.Field(basic, "Username (sent exactly as entered, including ! prefix)", profile.Username);
            var password = SettingsUi.Field(basic, "Password", profile.Password, secret: true);
            var protocol = SettingsUi.Choice(basic, "Protocol", profile.Protocol);
            var path = SettingsUi.Field(basic, "Remote base path", options.BasePath);
            var alternate = SettingsUi.Field(basic, "Alternate addresses (space separated host:port)", profile.AlternateAddresses);
            var fingerprint = SettingsUi.Field(basic, "Verified SFTP SHA256 host-key fingerprint", profile.SshHostKeyFingerprint);
            var security = SettingsUi.Form(); security.Margin = new Thickness(8);
            var listing = SettingsUi.Choice(security, "Directory listing", profile.ListingMode);
            var encoding = SettingsUi.Choice(security, "Text encoding", options.TextEncoding);
            var auth = SettingsUi.Choice(security, "AUTH command", options.AuthCommand);
            var invalidCert = SettingsUi.Check(security, "Allow invalid server TLS certificate", profile.AllowInvalidCertificate);
            var openSsl = SettingsUi.Check(security, "Use native OpenSSL TLS", options.UseOpenSslTls);
            var tls12 = SettingsUi.Check(security, "TLS 1.2 only", options.Tls12Only);
            var secureLists = SettingsUi.Check(security, "Encrypt file listings", options.SecureFileListings);
            var secureData = SettingsUi.Check(security, "Prefer encrypted data transfers", options.PreferTlsTransfers);
            var binary = SettingsUi.Check(security, "Force binary transfers", options.ForceBinaryMode);
            var slots = SettingsUi.Form(); slots.Margin = new Thickness(8);
            var login = SettingsUi.Field(slots, "Maximum login slots", options.MaxSlots.ToString());
            var up = SettingsUi.Field(slots, "Maximum upload slots", options.MaxUploadSlots.ToString());
            var down = SettingsUi.Field(slots, "Maximum download slots", options.MaxDownloadSlots.ToString());
            var priority = SettingsUi.Field(slots, "Site priority (higher value is preferred)", options.Priority.ToString());
            var allowUp = SettingsUi.Check(slots, "Allow upload", options.AllowUpload);
            var allowDown = SettingsUi.Check(slots, "Allow download", options.AllowDownload);
            SettingsUi.Note(slots, "Browsing and API probes use login slots too. Upload/download limits share that login budget.");
            var fxp = SettingsUi.Form(); fxp.Margin = new Thickness(8);
            var protection = SettingsUi.Choice(fxp, "FXP protection", options.FxpProtection);
            var role = SettingsUi.Choice(fxp, "FXP data role", options.FxpDataRole);
            var cpsv = SettingsUi.Check(fxp, "Use CPSV", options.UseCpsv);
            var pret = SettingsUi.Check(fxp, "Server requires PRET", options.NeedsPret);
            var cepr = SettingsUi.Check(fxp, "CEPR supported", options.CeprSupported);
            var xdupe = SettingsUi.Check(fxp, "Use XDUPE", options.UseXdupe);
            SettingsUi.Note(fxp, "Imported settings that are not shown here are preserved. Changes apply to new connections and jobs.");
            var panel = new DockPanel();
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            ConnectionProfile Value() => profile with
            {
                Name = name.Text?.Trim() ?? "", Host = host.Text?.Trim() ?? "", Port = SettingsUi.Number(port, "Port"), Username = user.Text ?? "", Password = password.Text ?? "",
                Protocol = (TransferProtocol)protocol.SelectedItem!, AlternateAddresses = alternate.Text?.Trim() ?? "", SshHostKeyFingerprint = fingerprint.Text?.Trim() ?? "",
                ListingMode = (DirectoryListingMode)listing.SelectedItem!, AllowInvalidCertificate = invalidCert.IsChecked == true,
                Options = options with { BasePath = LinuxSettings.RemotePath(path.Text ?? "/"), MaxSlots = SettingsUi.Number(login, "Login slots"),
                    MaxUploadSlots = SettingsUi.Number(up, "Upload slots"), MaxDownloadSlots = SettingsUi.Number(down, "Download slots"), Priority = SettingsUi.Number(priority, "Priority"),
                    AllowUpload = allowUp.IsChecked == true, AllowDownload = allowDown.IsChecked == true, TextEncoding = (FtpTextEncoding)encoding.SelectedItem!, AuthCommand = (FtpAuthCommand)auth.SelectedItem!,
                    UseOpenSslTls = openSsl.IsChecked == true, Tls12Only = tls12.IsChecked == true, SecureFileListings = secureLists.IsChecked == true,
                    PreferTlsTransfers = secureData.IsChecked == true, ForceBinaryMode = binary.IsChecked == true, FxpProtection = (FxpProtectionMode)protection.SelectedItem!,
                    FxpDataRole = (FxpDataRole)role.SelectedItem!, UseCpsv = cpsv.IsChecked == true, NeedsPret = pret.IsChecked == true, CeprSupported = cepr.IsChecked == true, UseXdupe = xdupe.IsChecked == true }
            };
            Guid Save()
            {
                var value = Value(); LinuxSettings.SaveSite(configuration, value); changed(); Reload(value.Id); report.Text = "Saved " + value.Name; return value.Id;
            }
            actions.Children.Add(Ui.Button("Save", () => { try { Save(); } catch (Exception ex) { report.Text = ex.Message; } return Task.CompletedTask; }));
            foreach (var target in new[] { (Label: "Connect left", Right: false), (Label: "Connect right", Right: true) })
                actions.Children.Add(Ui.Button(target.Label, async () => { try { var id = Save(); await connect(id, target.Right); Close(); } catch (Exception ex) { report.Text = ex.Message; } }));
            var delete = Ui.Button("Delete", async () =>
            {
                if (saved is null || !await SettingsUi.Confirm(this, "Delete site", "Delete " + profile.Name + " and its section mappings? Active transfers keep their current connection settings.")) return;
                try { LinuxSettings.DeleteSite(configuration, profile.Id); changed(); Reload(); Edit(null); report.Text = "Site deleted."; }
                catch (Exception ex) { report.Text = ex.Message; }
            }); delete.IsEnabled = saved is not null; actions.Children.Add(delete);
            DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
            panel.Children.Add(new TabControl { ItemsSource = new[] { SettingsUi.Tab("Connection", basic), SettingsUi.Tab("Slots & priority", slots), SettingsUi.Tab("Network / TLS", security), SettingsUi.Tab("FXP", fxp) } });
            holder.Content = panel;
        }
        add.Click += (_, _) => { names.SelectedItem = null; Edit(null); };
        names.SelectionChanged += (_, _) => { if (names.SelectedItem is SavedSite site) Edit(site); };
        Reload(); if (names.ItemsSource is IEnumerable<SavedSite> existing && existing.FirstOrDefault() is { } first) names.SelectedItem = first; else Edit(null);
        Content = root;
    }
}
