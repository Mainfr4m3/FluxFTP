using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Diagnostics;
using IoFtp.Core.Models;
using IoFtp.Engine.Abstractions;
using IoFtp.Engine.Models;
using IoFtp.Engine.Scheduling;
namespace IoFtp.Linux;
public sealed class MainWindow : Window
{
    private readonly ObservableCollection<Job> _jobs = [];
    private readonly ListBox _queue = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), Text = "Linux preview - saved sites use encrypted private Linux configuration" };
    private readonly GlobalTransferEngine _engine;
    private readonly FilePane _left, _right;
    private bool _closing;
    private LinuxApiServer? _api;
    private readonly SemaphoreSlim _apiGate = new(1, 1);
    private readonly LinuxConfiguration _configuration = new();
    private LinuxOptions _options = null!;
    private readonly Grid _body = new() { RowDefinitions = new RowDefinitions("3*,5,*,5,*") };
    private readonly Grid _manager = new() { Name = "ManagerPanel", ColumnDefinitions = new ColumnDefinitions("*,5,*") };
    private readonly DockPanel _queuePanel = new() { Name = "QueuePanel", Margin = new Thickness(8, 0) };
    private readonly DockPanel _logPanel = new() { Name = "LogPanel", Margin = new Thickness(8, 0, 8, 5) };
    private readonly StackPanel _metrics = new() { Name = "MetricsPanel", Spacing = 8, Margin = new Thickness(12, 8, 12, 12) };
    private readonly Dictionary<string, Button> _panelButtons = [];
    private readonly Dictionary<string, TextBlock> _metricValues = [];
    private readonly Stopwatch _sessionClock = Stopwatch.StartNew();
    private readonly HashSet<Guid> _completedJobs = [];
    private long _completedBytes;
    private readonly DispatcherTimer _metricsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pendingLog = new();
    private readonly Queue<string> _logLines = new();
    private int _logFlushScheduled;
    public MainWindow()
    {
        Title = "FluxFTP Linux - 0.1.0 preview 4"; Width = 1380; Height = 850; MinWidth = 950; MinHeight = 650;
        Background = Brush.Parse("#101820");
        _engine = new GlobalTransferEngine(new Executor(this));
        ApplyOptions();
        _engine.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateJobs);
        _left = new FilePane(Log); _right = new FilePane(Log);
        var root = new DockPanel();
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), Name = "MainToolbar" };
        toolbar.Children.Add(new TextBlock { Text = "FluxFTP", FontSize = 24, Foreground = Brush.Parse("#37C8B6"), VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(Ui.Button("Sites", async () => await new SitesWindow(_configuration, ReloadSites, async (id, right) =>
        {
            if (!_manager.IsVisible) TogglePanel("Manager");
            await (right ? _right : _left).SelectAndConnect(id);
        }).ShowDialog(this)));
        var tools = Ui.Button("Tools", () => Task.CompletedTask);
        var toolsMenu = new MenuFlyout();
        void Tool(string label, Func<Task> action)
        { var item = new MenuItem { Header = label }; item.Click += async (_, _) => { try { await action(); } catch (Exception ex) { Log(ex.Message); } }; toolsMenu.Items.Add(item); }
        Tool("Sections", async () => await new SectionsWindow(_configuration, ReloadSites).ShowDialog(this));
        Tool("API settings", () => ShowApi(this));
        Tool("Import Windows backup", async () => await ImportBackup(this));
        tools.Flyout = toolsMenu; toolbar.Children.Add(tools);
        foreach (var panel in new[] { "Metrics", "Manager", "Queue", "Log" })
        {
            var button = Ui.Button(panel, () => { TogglePanel(panel); return Task.CompletedTask; });
            _panelButtons[panel] = button; toolbar.Children.Add(button);
        }
        toolbar.Children.Add(Ui.Button("Global Settings", async () => await new GlobalSettingsWindow(_configuration.Load().Options, SaveOptions, ImportBackup, ShowApi).ShowDialog(this)));
        toolbar.Children.Add(Ui.Button("About", ShowAbout));
        foreach (var button in toolbar.Children.OfType<Button>()) { button.VerticalAlignment = VerticalAlignment.Center; button.Margin = new Thickness(6, 3, 0, 3); }
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        BuildMetrics(); DockPanel.SetDock(_metrics, Dock.Top); root.Children.Add(_metrics);
        _left.TransferRequested = () => Enqueue(_left, _right); _right.TransferRequested = () => Enqueue(_right, _left);
        _manager.Children.Add(_left); var divider = new GridSplitter { ResizeDirection = GridResizeDirection.Columns }; Grid.SetColumn(divider, 1); _manager.Children.Add(divider); Grid.SetColumn(_right, 2); _manager.Children.Add(_right); _body.Children.Add(_manager);
        var splitter = new GridSplitter { Name = "QueueSplitter", ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(splitter, 1); _body.Children.Add(splitter);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5) };
        actions.Children.Add(new TextBlock { Text = "TRANSFER QUEUE", Foreground = Brush.Parse("#37C8B6"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) });
        actions.Children.Add(Ui.Button("Start / Resume", () => { _engine.Resume(_jobs.Where(job => job.State is "Paused" or "Failed").Select(job => job.Id)); return Task.CompletedTask; }));
        actions.Children.Add(Ui.Button("Pause selected", () => { foreach (var job in Selected()) _engine.Pause(job.Id); return Task.CompletedTask; }));
        actions.Children.Add(Ui.Button("Remove selected", () => { foreach (var job in Selected()) { _engine.Remove(job.Id); _jobs.Remove(job); } return Task.CompletedTask; }));
        actions.Children.Add(Ui.Button("Clear completed", () => { foreach (var job in _jobs.Where(job => !job.ApiTransfer && job.State == "Completed").ToArray()) { _engine.Remove(job.Id); _jobs.Remove(job); } return Task.CompletedTask; }));
        foreach (var button in actions.Children.OfType<Button>()) button.Margin = new Thickness(0, 0, 5, 0);
        DockPanel.SetDock(actions, Dock.Top); _queuePanel.Children.Add(actions);
        _queue.ItemsSource = _jobs;
        _queue.ItemTemplate = new FuncDataTemplate<Job>((_, _) => new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(Job.Line)) });
        _queuePanel.Children.Add(_queue); Grid.SetRow(_queuePanel, 2); _body.Children.Add(_queuePanel);
        var logSplitter = new GridSplitter { Name = "LogSplitter", ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(logSplitter, 3); _body.Children.Add(logSplitter);
        var logHeader = new DockPanel { Margin = new Thickness(0, 4) };
        var logActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        logActions.Children.Add(Ui.Button("Copy", async () => { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(_log.Text ?? ""); }));
        logActions.Children.Add(Ui.Button("Clear", () => { _logLines.Clear(); _log.Text = ""; return Task.CompletedTask; }));
        DockPanel.SetDock(logActions, Dock.Right); logHeader.Children.Add(logActions);
        logHeader.Children.Add(new TextBlock { Text = "FTP-LOG", Foreground = Brush.Parse("#37C8B6"), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(logHeader, Dock.Top); _logPanel.Children.Add(logHeader); _logPanel.Children.Add(_log);
        Grid.SetRow(_logPanel, 4); _body.Children.Add(_logPanel); root.Children.Add(_body); Content = root;
        ApplyLayout(); UpdateMetrics();
        _metricsTimer.Tick += (_, _) => UpdateMetrics(); _metricsTimer.Start();
        if (Environment.GetEnvironmentVariable("FLUXFTP_RENDER_PATH") is { Length: > 0 } renderPath)
            Opened += (_, _) =>
            {
                Window capture = Environment.GetEnvironmentVariable("FLUXFTP_RENDER_WINDOW") switch
                {
                    "settings" => new GlobalSettingsWindow(_configuration.Load().Options, SaveOptions, ImportBackup, ShowApi),
                    "sites" => new SitesWindow(_configuration, ReloadSites, (_, _) => Task.CompletedTask),
                    "sections" => new SectionsWindow(_configuration, ReloadSites),
                    _ => this
                };
                if (capture != this) capture.Show(this);
                DispatcherTimer.RunOnce(() =>
                {
                    using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)capture.Bounds.Width, (int)capture.Bounds.Height));
                    image.Render(capture); image.Save(renderPath);
                    Console.WriteLine("PASS: native Linux window rendered to " + renderPath); if (capture != this) capture.Close(); Close();
                }, TimeSpan.FromSeconds(3));
            };
        Loaded += async (_, _) =>
        {
            try { await ConfigureApi(_configuration.Load().Options.Api ?? new(), false); }
            catch (Exception ex) { Log("API startup failed: " + ex.Message); }
        };
        Closing += async (_, e) =>
        {
            if (_closing) return;
            e.Cancel = true; _closing = true;
            _metricsTimer.Stop();
            await _apiGate.WaitAsync();
            try { if (_api is not null) { await _api.DisposeAsync(); _api = null; } }
            finally { _apiGate.Release(); }
            await _engine.DisposeAsync(); await _left.Disconnect(); await _right.Disconnect(); Close();
        };
    }
    private void ReloadSites() { _left.ReloadSites(); _right.ReloadSites(); }
    private Task ShowApi(Window owner) => new ApiSettingsWindow(_configuration.Load().Options.Api ?? new(), _api?.Endpoint ?? "Disabled", _api?.Fingerprint ?? "", options => ConfigureApi(options, true)).ShowDialog(owner);
    private async Task<bool> ImportBackup(Window owner)
    {
        var dialog = new BackupImportWindow(); await dialog.ShowDialog(owner);
        if (!dialog.Imported) return false;
        ReloadSites(); ApplyOptions(); await _left.ApplyLocalOptions(); await _right.ApplyLocalOptions();
        Log("Windows backup imported. Saved sites, sections and compatible transfer settings are available."); return true;
    }
    private async Task SaveOptions(LinuxOptions edited)
    {
        var previousPath = _options.DownloadPath;
        LinuxSettings.SaveOptions(_configuration, edited); ApplyOptions();
        if (previousPath != _options.DownloadPath) { await _left.ApplyLocalOptions(); await _right.ApplyLocalOptions(); }
        UpdateJobs(); Log("Global settings saved.");
    }
    private Task ShowAbout()
    {
        var form = SettingsUi.Form();
        form.Children.Add(new TextBlock { Text = "FluxFTP Linux", FontSize = 28, Foreground = Brush.Parse("#37C8B6") });
        SettingsUi.Note(form, "0.1.0 preview 4\n\nFTP, FTPS, SFTP and FXP with the shared FluxFTP transfer engine.\nSites, sections, global transfer settings, live Metrics, Windows backup import and the RaceTrade HTTPS API.");
        form.Children.Add(Ui.Button("GitHub · Mainfr4m3/FluxFTP", () => { try { Process.Start(new ProcessStartInfo("https://github.com/Mainfr4m3/FluxFTP") { UseShellExecute = true }); } catch (Exception ex) { Log(ex.Message); } return Task.CompletedTask; }));
        return new Window { Title = "About FluxFTP Linux", Width = 580, Height = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = form }.ShowDialog(this);
    }
    private void TogglePanel(string name)
    {
        var layout = _options.Layout ?? new();
        layout = name switch { "Metrics" => layout with { Metrics = !layout.Metrics }, "Manager" => layout with { Manager = !layout.Manager }, "Queue" => layout with { Queue = !layout.Queue }, "Log" => layout with { Log = !layout.Log }, _ => layout };
        try { _configuration.Update(state => state with { Options = state.Options with { Layout = layout } }); _options = _options with { Layout = layout }; ApplyLayout(); }
        catch (Exception ex) { Log("Could not save panel layout: " + ex.Message); }
    }
    private void ApplyLayout()
    {
        var layout = _options.Layout ?? new();
        _metrics.IsVisible = layout.Metrics; _manager.IsVisible = layout.Manager; _queuePanel.IsVisible = layout.Queue; _logPanel.IsVisible = layout.Log;
        _body.RowDefinitions[0].Height = layout.Manager ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
        _body.RowDefinitions[2].Height = layout.Queue ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _body.RowDefinitions[4].Height = layout.Log ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        foreach (var row in new[] { 1, 3 })
        {
            var visible = row == 1 ? layout.Manager && (layout.Queue || layout.Log) : layout.Queue && layout.Log;
            _body.RowDefinitions[row].Height = new GridLength(visible ? 5 : 0);
            _body.Children.OfType<GridSplitter>().Single(splitter => Grid.GetRow(splitter) == row).IsVisible = visible;
        }
        foreach (var (name, button) in _panelButtons) ToolTip.SetTip(button, (name switch { "Metrics" => layout.Metrics, "Manager" => layout.Manager, "Queue" => layout.Queue, _ => layout.Log } ? "Hide " : "Show ") + name);
    }
    private void BuildMetrics()
    {
        _metrics.Children.Add(new TextBlock { Text = "FLUXFTP METRICS", FontSize = 21, Foreground = Brush.Parse("#37C8B6") });
        var cards = new WrapPanel();
        foreach (var name in new[] { "COMPLETED DATA", "COMPLETED FILES", "CURRENT SPEED", "QUEUED / ACTIVE", "SESSION RUNNING" })
        {
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock { Text = name, FontSize = 10, Foreground = Brush.Parse("#8EACBD") });
            var value = new TextBlock { FontSize = 19, Foreground = Brush.Parse("#37C8B6"), Text = "—" }; _metricValues[name] = value; content.Children.Add(value);
            cards.Children.Add(new Border { Child = content, Background = Brush.Parse("#17232D"), BorderBrush = Brush.Parse("#304655"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(12, 8), Width = 175, Margin = new Thickness(0, 0, 7, 5) });
        }
        _metrics.Children.Add(cards);
        var api = new TextBlock { Name = "ApiSummary", Foreground = Brush.Parse("#8EACBD") }; _metricValues["API"] = api; _metrics.Children.Add(api);
    }
    private void UpdateMetrics()
    {
        if (_metricValues.Count == 0) return;
        _metricValues["COMPLETED DATA"].Text = _completedBytes >= 1_000_000_000 ? $"{_completedBytes / 1_000_000_000d:0.0} GB" : $"{_completedBytes / 1_000_000d:0.0} MB";
        _metricValues["COMPLETED FILES"].Text = _completedJobs.Count.ToString("N0");
        _metricValues["CURRENT SPEED"].Text = $"{_jobs.Sum(job => job.Speed) / 1_000_000d:0.00} MB/s";
        _metricValues["QUEUED / ACTIVE"].Text = $"{_jobs.Count(job => job.State == "Queued")} / {_jobs.Count(job => job.State == "Running")}";
        _metricValues["SESSION RUNNING"].Text = $"{(int)_sessionClock.Elapsed.TotalHours:00}:{_sessionClock.Elapsed.Minutes:00}:{_sessionClock.Elapsed.Seconds:00}";
        _metricValues["API"].Text = "API: " + (_api?.Endpoint ?? "Disabled") + "   ·   Metrics count completed file sizes; FXP reports progress after completion.";
    }
    internal Task StartApiCheck(LinuxApiOptions options) => ConfigureApi(options, true);
    internal string ApiFingerprint => _api?.Fingerprint ?? "";
    private LinuxApiServer CreateApi() => new(_configuration,
        file => Dispatcher.UIThread.InvokeAsync(() => EnqueueApi(file)).GetTask(),
        ids => Dispatcher.UIThread.InvokeAsync(() => ApiJobs(ids)).GetTask(),
        id => Dispatcher.UIThread.InvokeAsync(() => { _engine.Remove(id); var job = _jobs.FirstOrDefault(job => job.Id == id); if (job is not null) _jobs.Remove(job); }).GetTask(),
        Log, () => Dispatcher.UIThread.Post(() => { _left.ReloadSites(); _right.ReloadSites(); }));
    private async Task ConfigureApi(LinuxApiOptions options, bool save)
    {
        options.Validate(); await _apiGate.WaitAsync();
        var previous = _configuration.Load().Options.Api ?? new();
        try
        {
            if (_closing) return;
            if (options.Enabled && (!previous.Enabled || options.Port != previous.Port))
            {
                var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Parse(options.Address), options.Port);
                try { probe.Start(); } finally { probe.Stop(); }
            }
            if (_api is not null) { await _api.DisposeAsync(); _api = null; }
            var candidate = CreateApi();
            try
            {
                await candidate.StartAsync(options);
                if (save) _configuration.Update(state => state with { Options = state.Options with { Api = options } });
                _api = candidate;
            }
            catch
            {
                await candidate.DisposeAsync();
                if (previous.Enabled) { var restored = CreateApi(); try { await restored.StartAsync(previous); _api = restored; } catch { await restored.DisposeAsync(); } }
                throw;
            }
        }
        finally { _apiGate.Release(); }
    }
    private Guid EnqueueApi(LinuxApiFile file)
    {
        if (_closing) throw new OperationCanceledException("Client is closing");
        if (_jobs.Any(job => job.Target?.Id == file.Target.Id && job.Destination == file.DestinationPath && job.State is "Queued" or "Running" or "Paused")) return Guid.Empty;
        foreach (var profile in new[] { file.Source, file.Target })
            _engine.RegisterOrUpdateSite(new(profile.Id, profile.Name, profile.EffectiveOptions.MaxSlots, profile.EffectiveOptions.AllowDownload ? profile.EffectiveOptions.MaxDownloadSlots : 0, profile.EffectiveOptions.AllowUpload ? profile.EffectiveOptions.MaxUploadSlots : 0, profile.EffectiveOptions.Priority));
        var job = new Job { File = new(System.IO.Path.GetFileName(file.SourcePath), file.SourcePath, false, file.Size), Source = file.Source, Target = file.Target, Destination = file.DestinationPath, ApiTransfer = true };
        _jobs.Add(job);
        _engine.Enqueue([new(job.Id, job.Id, job.File.Name, file.Source.Id, file.Target.Id, file.SourcePath, file.DestinationPath, file.Size,
            FilePriorityRank: job.File.Name.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase) ? 0 : job.File.Name.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase) ? 2 : 1)]);
        return job.Id;
    }
    private List<LinuxApiJob> ApiJobs(IEnumerable<Guid> ids)
    {
        var snapshot = _engine.Snapshot().ToDictionary(status => status.Item.Id);
        return ids.Select(id => snapshot.TryGetValue(id, out var status) ? new LinuxApiJob(id, status.State == TransferWorkState.Running ? "Transferring" : status.State.ToString(), _jobs.FirstOrDefault(job => job.Id == id)?.Bytes ?? 0, status.Error ?? "") : new(id, "Missing", 0, "Job removed")).ToList();
    }
    private void ApplyOptions()
    {
        var options = _configuration.Load().Options; _options = options;
        _engine.ConfigureLocalSlots(options.MaxLocalDownloads, options.MaxLocalUploads, options.SerializeLocalTransfers);
    }
    private Job[] Selected() => _queue.SelectedItems?.Cast<Job>().ToArray() ?? [];
    private Task Enqueue(FilePane source, FilePane target)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target.CurrentPath)) throw new IOException("Open the destination folder first.");
            foreach (var file in source.Selected())
            {
                if (file.Directory) { Log("Folder transfer is not available in this preview: " + file.Name); continue; }
                if (file.Name is "." or ".." || file.Name.IndexOfAny(['/', '\\']) >= 0) throw new IOException("Invalid filename from server.");
                var destination = target.Profile is null ? Path.Combine(target.CurrentPath, file.Name) : target.CurrentPath.TrimEnd('/') + "/" + file.Name;
                if (source.Profile is null && target.Profile is null && Path.GetFullPath(file.Path) == Path.GetFullPath(destination)) throw new IOException("Source and destination are the same file.");
                if (_jobs.Any(job => job.Destination == destination && job.Target?.Id == target.Profile?.Id && job.State is "Queued" or "Running" or "Paused")) { Log("Already queued: " + file.Name); continue; }
                foreach (var profile in new[] { source.Profile, target.Profile }.OfType<ConnectionProfile>())
                    _engine.RegisterOrUpdateSite(new SitePolicy(profile.Id, profile.Name, profile.EffectiveOptions.MaxSlots,
                        profile.EffectiveOptions.AllowDownload ? profile.EffectiveOptions.MaxDownloadSlots : 0, profile.EffectiveOptions.AllowUpload ? profile.EffectiveOptions.MaxUploadSlots : 0, profile.EffectiveOptions.Priority));
                var job = new Job { File = file, Source = source.Profile, Target = target.Profile, Destination = destination, Order = _jobs.Count };
                _jobs.Add(job);
                _engine.Enqueue([new TransferWorkItem(job.Id, job.Id, file.Name, source.Profile?.Id, target.Profile?.Id, file.Path, destination, file.Size, ManualQueueOrder: job.Order)]);
            }
        }
        catch (Exception ex) { Log(ex.Message); }
        return Task.CompletedTask;
    }
    private void Log(string message)
    {
        if (Environment.GetEnvironmentVariable("FLUXFTP_API_TEST_LOG") == "1") Console.WriteLine(message);
        _pendingLog.Enqueue($"{DateTime.Now:yyyy.MM.dd-HH:mm:ss} {message}");
        if (Interlocked.Exchange(ref _logFlushScheduled, 1) == 0) Dispatcher.UIThread.Post(FlushLog, DispatcherPriority.Background);
    }
    private void FlushLog()
    {
        Interlocked.Exchange(ref _logFlushScheduled, 0);
        while (_pendingLog.TryDequeue(out var text)) foreach (var line in text.Split('\n')) _logLines.Enqueue(line.TrimEnd('\r'));
        var size = _logLines.Sum(line => line.Length);
        while (_logLines.Count > _options.LogBufferLines || size > 2_000_000) size -= _logLines.Dequeue().Length;
        _log.Text = string.Join('\n', _logLines);
        _log.CaretIndex = _log.Text.Length;
    }
    private void UpdateJobs()
    {
        foreach (var status in _engine.Snapshot())
        {
            var job = _jobs.FirstOrDefault(job => job.Id == status.Item.Id); if (job is null) continue;
            job.State = status.State.ToString(); job.Error = status.Error ?? "";
            if (status.State != TransferWorkState.Running) job.Speed = 0;
            if (status.State == TransferWorkState.Completed && _completedJobs.Add(job.Id)) _completedBytes += job.File.Size;
            job.Refresh();
        }
        if (_options.ClearCompletedManualTransfers)
            foreach (var job in _jobs.Where(job => !job.ApiTransfer && job.State == "Completed").ToArray()) { _engine.Remove(job.Id); _jobs.Remove(job); }
        UpdateMetrics();
        _status.Text = $"Jobs: {_jobs.Count(job => job.State == "Running")} active / {_jobs.Count} total   Speed: {_jobs.Sum(job => job.Speed) / 1_000_000d:0.00} MB/s";
    }
    private sealed class Executor(MainWindow window) : ITransferExecutor
    {
        public async Task ExecuteAsync(TransferWorkItem item, CancellationToken cancellationToken)
        {
            var job = await Dispatcher.UIThread.InvokeAsync(() => window._jobs.Single(job => job.Id == item.Id));
            var clock = Stopwatch.StartNew(); long lastBytes = job.Bytes; var lastTime = clock.Elapsed;
            var progress = new InlineProgress(bytes =>
            {
                var now = clock.Elapsed;
                if (now - lastTime < TimeSpan.FromMilliseconds(250) && bytes != job.File.Size) return;
                var speed = (long)(Math.Max(0, bytes - lastBytes) / Math.Max(0.001, (now - lastTime).TotalSeconds));
                lastBytes = bytes; lastTime = now;
                Dispatcher.UIThread.Post(() => { job.Bytes = bytes; job.Speed = speed; job.Refresh(); window.UpdateJobs(); });
            });
            var options = window._configuration.Load().Options;
            using var runtime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (options.MaxTransferRuntimeMinutes > 0) runtime.CancelAfter(TimeSpan.FromMinutes(options.MaxTransferRuntimeMinutes));
            await Transfers.Run(job, progress, window.Log, runtime.Token, options.StarterTimeoutSeconds);
        }
    }
}
internal sealed class InlineProgress(Action<long> callback) : IProgress<long> { public void Report(long value) => callback(value); }
