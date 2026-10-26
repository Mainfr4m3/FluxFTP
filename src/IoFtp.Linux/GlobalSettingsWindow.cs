using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using IoFtp.Core.Models;
namespace IoFtp.Linux;
internal static class SettingsUi
{
    public static StackPanel Form() => new() { Spacing = 10, Margin = new Thickness(16) };
    public static TextBox Field(StackPanel form, string label, string text, string? name = null, bool secret = false)
    {
        form.Children.Add(new TextBlock { Text = label });
        var field = new TextBox { Text = text, Name = name, PasswordChar = secret ? '*' : '\0', HorizontalAlignment = HorizontalAlignment.Stretch };
        form.Children.Add(field); return field;
    }
    public static ComboBox Choice<T>(StackPanel form, string label, T selected) where T : struct, Enum
    {
        form.Children.Add(new TextBlock { Text = label });
        var field = new ComboBox { ItemsSource = Enum.GetValues<T>(), SelectedItem = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        form.Children.Add(field); return field;
    }
    public static CheckBox Check(StackPanel form, string label, bool value)
    { var field = new CheckBox { Content = label, IsChecked = value }; form.Children.Add(field); return field; }
    public static void Note(StackPanel form, string text) => form.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#8EACBD") });
    public static TabItem Tab(string label, Control content) => new() { Header = label, Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
    public static int Number(TextBox field, string label) => int.TryParse(field.Text, out var number) ? number : throw new ArgumentException(label + " must be a whole number.");
    public static async Task<bool> Confirm(Window owner, string title, string text)
    {
        var dialog = new Window { Title = title, Width = 440, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var form = Form(); Note(form, text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Ui.Button("Delete", () => { dialog.Close(true); return Task.CompletedTask; }));
        buttons.Children.Add(Ui.Button("Cancel", () => { dialog.Close(false); return Task.CompletedTask; }));
        form.Children.Add(buttons); dialog.Content = form; return await dialog.ShowDialog<bool>(owner);
    }
}
internal sealed class GlobalSettingsWindow : Window
{
    public GlobalSettingsWindow(LinuxOptions options, Func<LinuxOptions, Task> save, Func<Window, Task<bool>> import, Func<Window, Task> api)
    {
        Title = "Global Settings"; Width = 740; Height = 650; MinWidth = 580; MinHeight = 470; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        var title = new TextBlock { Text = "GLOBAL SETTINGS", FontSize = 24, Foreground = Brush.Parse("#37C8B6"), Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var local = SettingsUi.Form();
        var path = SettingsUi.Field(local, "Local download folder", options.DownloadPath, "DownloadPath");
        local.Children.Add(Ui.Button("Browse...", async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Local download folder", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } selected) path.Text = selected;
        }));
        var downloads = SettingsUi.Field(local, "Local download slots (0 = disabled)", options.MaxLocalDownloads.ToString(), "DownloadSlots");
        var uploads = SettingsUi.Field(local, "Local upload slots (0 = disabled)", options.MaxLocalUploads.ToString(), "UploadSlots");
        var serial = SettingsUi.Check(local, "One local transfer at a time (downloads, uploads and disk copies)", options.SerializeLocalTransfers);
        SettingsUi.Note(local, "Site login and upload/download limits also apply. New connections and transfers use saved settings.");
        var jobs = SettingsUi.Form();
        var starter = SettingsUi.Field(jobs, "Connection / slot waiting timeout (seconds)", options.StarterTimeoutSeconds.ToString(), "StarterTimeout");
        var runtime = SettingsUi.Field(jobs, "Maximum transfer runtime (minutes, 0 = unlimited)", options.MaxTransferRuntimeMinutes.ToString(), "TransferRuntime");
        var history = SettingsUi.Field(jobs, "Log buffer history (lines)", options.LogBufferLines.ToString(), "LogHistory");
        var clear = SettingsUi.Check(jobs, "Automatically remove completed manual transfers from the queue", options.ClearCompletedManualTransfers);
        SettingsUi.Note(jobs, "API transfers retain their completion state for the controlling client. Changing these settings does not restart the API.");
        var proxyForm = SettingsUi.Form(); var proxy = options.Proxy ?? new();
        var type = SettingsUi.Choice(proxyForm, "Global proxy type", proxy.Type);
        var host = SettingsUi.Field(proxyForm, "Proxy host", proxy.Host);
        var port = SettingsUi.Field(proxyForm, "Proxy port", proxy.Port.ToString());
        var user = SettingsUi.Field(proxyForm, "Proxy username", proxy.Username);
        var password = SettingsUi.Field(proxyForm, "Proxy password", proxy.Password, secret: true);
        var dns = SettingsUi.Check(proxyForm, "Resolve host names through proxy", proxy.ProxyDns);
        var data = SettingsUi.Check(proxyForm, "Use proxy for data connections", proxy.UseForData);
        SettingsUi.Note(proxyForm, "Used by sites without a site-specific proxy. Imported site proxy settings are retained.");
        var backup = SettingsUi.Form();
        SettingsUi.Note(backup, "Import an encrypted .fluxbackup exported from FluxFTP Windows. Sites, credentials, sections and compatible transfer settings are merged.");
        backup.Children.Add(Ui.Button("Import Windows backup...", async () => { if (await import(this)) Close(); }));
        SettingsUi.Note(backup, "Linux configuration folder: " + SecretStore.DirectoryPath + "\nFor a manual Linux backup, preserve state.enc and secret.key together.");
        var apiForm = SettingsUi.Form();
        SettingsUi.Note(apiForm, "Configure the RaceTrade/CBFTP-compatible HTTPS API, its password, listen address and race scheduling limits.");
        apiForm.Children.Add(Ui.Button("API settings...", () => api(this)));
        var report = new TextBlock { Name = "SettingsError", TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FFB179"), Margin = new Thickness(0, 8) };
        var footer = new StackPanel { Spacing = 8 }; footer.Children.Add(report);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var saveButton = Ui.Button("Save", async () =>
        {
            try
            {
                var kind = (ProxyType)type.SelectedItem!;
                var edited = options with { DownloadPath = path.Text?.Trim() ?? "", MaxLocalDownloads = SettingsUi.Number(downloads, "Download slots"),
                    MaxLocalUploads = SettingsUi.Number(uploads, "Upload slots"), SerializeLocalTransfers = serial.IsChecked == true,
                    StarterTimeoutSeconds = SettingsUi.Number(starter, "Connection timeout"), MaxTransferRuntimeMinutes = SettingsUi.Number(runtime, "Transfer runtime"),
                    LogBufferLines = SettingsUi.Number(history, "Log history"), ClearCompletedManualTransfers = clear.IsChecked == true,
                    Proxy = new ProxyConfiguration(kind, host.Text?.Trim() ?? "", kind == ProxyType.None ? 0 : SettingsUi.Number(port, "Proxy port"), user.Text ?? "", password.Text ?? "", dns.IsChecked == true, data.IsChecked == true) };
                LinuxSettings.Validate(edited);
                if (File.Exists(edited.DownloadPath)) throw new ArgumentException("Download folder points to a file.");
                await save(edited); Close();
            }
            catch (Exception ex) { report.Text = ex.Message; }
        });
        saveButton.Name = "SaveSettings"; actions.Children.Add(saveButton);
        actions.Children.Add(Ui.Button("Cancel", () => { Close(); return Task.CompletedTask; }));
        footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new TabControl { Name = "SettingsTabs", ItemsSource = new[] { SettingsUi.Tab("Local", local), SettingsUi.Tab("Jobs & log", jobs), SettingsUi.Tab("Proxy", proxyForm), SettingsUi.Tab("Backup/Import", backup), SettingsUi.Tab("API", apiForm) } });
        Content = root;
    }
}
