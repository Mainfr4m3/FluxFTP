using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
namespace IoFtp.Linux;
internal sealed class ApiSettingsWindow : Window
{
    public ApiSettingsWindow(LinuxApiOptions options, string endpoint, string fingerprint, Func<LinuxApiOptions, Task> apply)
    {
        Title = "RaceTrade API"; Width = 650; Height = 650;
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "HTTPS / CBFTP-compatible API", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "Current: " + endpoint, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var enabled = new CheckBox { Content = "Enable API", IsChecked = options.Enabled }; panel.Children.Add(enabled);
        TextBox Field(string label, string value, bool secret = false)
        { panel.Children.Add(new TextBlock { Text = label }); var field = new TextBox { Text = value, PasswordChar = secret ? '*' : '\0' }; panel.Children.Add(field); return field; }
        var address = Field("Listen address: 127.0.0.1 for local use, 0.0.0.0 for LAN access", options.Address);
        var port = Field("HTTPS port", options.Port.ToString());
        var password = Field("API password (8-128 printable ASCII characters)", options.Password, true);
        var limit = Field("Maximum active API jobs", options.MaxSpreadJobs.ToString());
        var recheck = Field("Race recheck seconds", options.RecheckSeconds.ToString());
        var timeout = Field("Maximum API job lifetime, minutes", options.TimeoutMinutes.ToString());
        panel.Children.Add(new TextBlock { Text = "Saving restarts the API and stops its active jobs. Manual queue transfers continue.\nRaceTrade uses HTTPS Basic authentication with an empty username and this password.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (fingerprint.Length > 0) panel.Children.Add(new TextBlock { Text = "TLS certificate SHA256: " + fingerprint, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var report = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; panel.Children.Add(report);
        panel.Children.Add(Ui.Button("Save", async () =>
        {
            try
            {
                if (!int.TryParse(port.Text, out var parsedPort) || !int.TryParse(limit.Text, out var parsedLimit) || !int.TryParse(recheck.Text, out var parsedRecheck) || !int.TryParse(timeout.Text, out var parsedTimeout)) throw new ArgumentException("Numeric fields are invalid.");
                var value = new LinuxApiOptions(enabled.IsChecked == true, address.Text?.Trim() ?? "", parsedPort, password.Text ?? "", parsedLimit, parsedRecheck, parsedTimeout);
                value.Validate(); await apply(value); Close();
            }
            catch (Exception ex) { report.Text = ex.Message; }
        }));
        panel.Children.Add(Ui.Button("Cancel", () => { Close(); return Task.CompletedTask; }));
        Content = new ScrollViewer { Content = panel };
    }
}
