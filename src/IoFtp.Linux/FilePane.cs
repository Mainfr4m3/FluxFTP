using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
namespace IoFtp.Linux;
internal sealed class FilePane : UserControl
{
    private readonly LinuxConfiguration _configuration = new();
    private readonly ComboBox _sites = new() { PlaceholderText = "Saved sites", Width = 240 };
    private readonly ComboBox _sections = new() { PlaceholderText = "Sections", Width = 160 };
    private ConnectionProfile? _selectedSite;
    private bool _loading;
    private readonly TextBox _path = new() { Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
    private readonly TextBox _host = new() { Watermark = "Host", Width = 150 };
    private readonly TextBox _user = new() { Watermark = "Username", Width = 110 };
    private readonly TextBox _password = new() { Watermark = "Password", PasswordChar = '*', Width = 155 };
    private readonly TextBox _port = new() { Text = "21", Width = 65 };
    private readonly TextBox _fingerprint = new() { Watermark = "SFTP verified SHA256 host-key fingerprint" };
    private readonly ComboBox _protocol = new() { ItemsSource = Enum.GetValues<TransferProtocol>(), SelectedIndex = 1, Width = 140 };
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Action<string> _log;
    private FtpRemoteSession? _session;
    public ConnectionProfile? Profile { get; private set; }
    public string CurrentPath { get; private set; } = "";
    public FilePane(Action<string> log)
    {
        _log = log;
        var panel = new DockPanel { Margin = new Thickness(5) };
        var controls = new StackPanel { Spacing = 5 };
        var saved = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        _sites.ItemTemplate = new FuncDataTemplate<ConnectionProfile>((_, _) => new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(ConnectionProfile.Name)) });
        saved.Children.Add(_sites); saved.Children.Add(_sections);
        saved.Children.Add(Ui.Button("Save site", () =>
        {
            try { var profile = EditedProfile(); _configuration.SaveSite(profile); ReloadSites(); _sites.SelectedItem = ((IEnumerable<ConnectionProfile>)_sites.ItemsSource!).First(site => site.Id == profile.Id); _log("Saved site: " + profile.Name); }
            catch (Exception ex) { _log(ex.Message); }
            return Task.CompletedTask;
        }));
        controls.Children.Add(saved);
        _sites.SelectionChanged += (_, _) =>
        {
            if (_loading || _sites.SelectedItem is not ConnectionProfile site) return;
            _selectedSite = site; _host.Text = site.Host; _port.Text = site.Port.ToString(); _user.Text = site.Username;
            _password.Text = site.Password; _protocol.SelectedItem = site.Protocol; _fingerprint.Text = site.SshHostKeyFingerprint;
        };
        _sections.SelectionChanged += async (_, _) =>
        {
            if (_loading || _sections.SelectedItem is not LinuxSection section) return;
            var name = Profile?.Name ?? _selectedSite?.Name;
            var entry = section.SitePaths.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry.Value is null) { _log("Section has no path for the selected site."); return; }
            _path.Text = entry.Value;
            if (Profile is not null) await Refresh();
        };
        ReloadSites();
        _path.Text = _configuration.Load().Options.DownloadPath;
        var connection = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[] { _host, _port, _user, _password, _protocol }) connection.Children.Add(control);
        controls.Children.Add(connection); controls.Children.Add(_fingerprint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        actions.Children.Add(Ui.Button("Connect", Connect));
        actions.Children.Add(Ui.Button("Local", async () => { await Disconnect(); _path.Text = _configuration.Load().Options.DownloadPath; await Refresh(); }));
        actions.Children.Add(Ui.Button("Up", async () => { _path.Text = Profile is null ? System.IO.Path.GetDirectoryName(CurrentPath.TrimEnd(System.IO.Path.DirectorySeparatorChar)) ?? "/" : RemoteParent(CurrentPath); await Refresh(); }));
        actions.Children.Add(Ui.Button("Refresh", Refresh)); controls.Children.Add(actions); controls.Children.Add(_path);
        _path.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await Refresh(); };
        DockPanel.SetDock(controls, Dock.Top); panel.Children.Add(controls); panel.Children.Add(_list); Content = panel;
        _list.DoubleTapped += async (_, _) => { if (_list.SelectedItem is FileItem { Directory: true } item) { _path.Text = item.Path; await Refresh(); } };
        Loaded += async (_, _) => await Refresh();
    }
    public async Task ApplyLocalOptions()
    {
        if (Profile is not null) return;
        var path = _configuration.Load().Options.DownloadPath;
        if (Directory.Exists(path)) { _path.Text = path; await Refresh(); }
    }
    public void ReloadSites()
    {
        _loading = true;
        try { var state = _configuration.Load(); _sites.ItemsSource = state.Sites.Select(site => site.Restore()).OrderBy(site => site.Name).ToList(); _sections.ItemsSource = state.Sections; }
        finally { _loading = false; }
    }
    private ConnectionProfile EditedProfile()
    {
        if (!int.TryParse(_port.Text, out var port) || port is < 1 or > 65535 || string.IsNullOrWhiteSpace(_host.Text)) throw new InvalidDataException("Enter a host and valid port.");
        var state = _configuration.Load();
        var profile = _selectedSite ?? new ConnectionProfile(Guid.NewGuid(), _host.Text.Trim(), _host.Text.Trim(), port, _user.Text ?? "", (TransferProtocol)_protocol.SelectedItem!);
        return profile with { Host = _host.Text.Trim(), Port = port, Username = _user.Text ?? "", Password = _password.Text ?? "",
            Protocol = (TransferProtocol)_protocol.SelectedItem!, SshHostKeyFingerprint = _fingerprint.Text ?? "", Proxy = profile.Proxy ?? state.Options.Proxy };
    }
    public List<FileItem> Selected() => _list.SelectedItems?.Cast<FileItem>().ToList() ?? [];
    private async Task Connect()
    {
        if (!int.TryParse(_port.Text, out var port) || port is < 1 or > 65535 || string.IsNullOrWhiteSpace(_host.Text)) { _log("Enter a host and valid port."); return; }
        await _gate.WaitAsync();
        try
        {
            await Disconnect();
            var protocol = (TransferProtocol)_protocol.SelectedItem!;
            if (protocol == TransferProtocol.Sftp && string.IsNullOrWhiteSpace(_fingerprint.Text)) throw new InvalidOperationException("Enter the verified SFTP host-key fingerprint first.");
            var profile = EditedProfile();
            var session = new FtpRemoteSession(); session.ProtocolMessage += _log;
            try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await session.ConnectAsync(profile, timeout.Token); }
            catch { await session.DisposeAsync(); throw; }
            _session = session; Profile = profile; _path.Text = _sections.SelectedItem is LinuxSection section ? section.SitePaths.FirstOrDefault(pair => pair.Key.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)).Value ?? profile.EffectiveOptions.BasePath : profile.EffectiveOptions.BasePath; _log("Connected: " + profile.Name);
        }
        catch (Exception ex) { _log(ex.Message); }
        finally { _gate.Release(); }
        await Refresh();
    }
    public async Task Refresh()
    {
        await _gate.WaitAsync();
        try
        {
            var path = _path.Text ?? "/";
            List<FileItem> items;
            if (Profile is null)
            {
                path = System.IO.Path.GetFullPath(path);
                items = await Task.Run(() => new DirectoryInfo(path).EnumerateFileSystemInfos().Select(file => new FileItem(file.Name, file.FullName, file is DirectoryInfo, file is FileInfo info ? info.Length : 0)).ToList());
            }
            else
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                items = (await _session!.ListAsync(path, timeout.Token)).Where(entry => entry.Name is not "." and not "..")
                    .Select(entry => new FileItem(entry.Name, entry.FullPath, entry.IsDirectory, entry.Size ?? 0)).ToList();
            }
            CurrentPath = path; _list.ItemsSource = items.OrderByDescending(item => item.Directory).ThenBy(item => item.Name, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) { _log(ex.Message); }
        finally { _gate.Release(); }
    }
    public async Task Disconnect() { var session = _session; _session = null; Profile = null; if (session is not null) await session.DisposeAsync(); }
    private static string RemoteParent(string path) { var i = path.TrimEnd('/').LastIndexOf('/'); return i <= 0 ? "/" : path[..i]; }
}
internal static class Ui
{
    public static Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 6) };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } finally { button.IsEnabled = true; } }; return button;
    }
}
