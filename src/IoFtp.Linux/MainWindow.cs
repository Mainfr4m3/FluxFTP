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
    public MainWindow()
    {
        Title = "FluxFTP Linux - 0.1.0 preview 3"; Width = 1280; Height = 850; MinWidth = 950; MinHeight = 650;
        Background = Brush.Parse("#101820");
        _engine = new GlobalTransferEngine(new Executor(this));
        ApplyOptions();
        _engine.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateJobs);
        _left = new FilePane(Log); _right = new FilePane(Log);
        var root = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8) };
        toolbar.Children.Add(new TextBlock { Text = "FluxFTP", FontSize = 24, Foreground = Brush.Parse("#37C8B6"), VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(Ui.Button("Transfer left -> right", () => Enqueue(_left, _right)));
        toolbar.Children.Add(Ui.Button("Transfer right -> left", () => Enqueue(_right, _left)));
        toolbar.Children.Add(Ui.Button("Import Windows backup", async () =>
        {
            var dialog = new BackupImportWindow(); await dialog.ShowDialog(this);
            if (dialog.Imported) { _left.ReloadSites(); _right.ReloadSites(); ApplyOptions(); await _left.ApplyLocalOptions(); await _right.ApplyLocalOptions(); Log("Windows backup imported. Saved sites and sections are available in both panes; Visi remains disabled."); }
        }));
        toolbar.Children.Add(Ui.Button("API", async () => await new ApiSettingsWindow(_configuration.Load().Options.Api ?? new(), _api?.Endpoint ?? "Disabled", _api?.Fingerprint ?? "", options => ConfigureApi(options, true)).ShowDialog(this)));
        toolbar.Children.Add(Ui.Button("About", async () => await new Window { Title = "About FluxFTP Linux", Width = 520, Height = 220, Content = new TextBlock { Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap, Text = "FluxFTP Linux 0.1.0 preview 3\nSame FTP/FTPS/SFTP and transfer engine as FluxFTP Windows.\nWindows backup import, saved sites and sections are supported. Bridge, full settings and recursive folder transfers are not ported yet." } }.ShowDialog(this)));
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var body = new Grid { RowDefinitions = new RowDefinitions("*,5,220,5,150") };
        var panes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,*") };
        panes.Children.Add(_left); var divider = new GridSplitter { ResizeDirection = GridResizeDirection.Columns }; Grid.SetColumn(divider, 1); panes.Children.Add(divider); Grid.SetColumn(_right, 2); panes.Children.Add(_right); body.Children.Add(panes);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(splitter, 1); body.Children.Add(splitter);
        var queuePanel = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(5) };
        actions.Children.Add(Ui.Button("Start / Resume", () => { _engine.Resume(_jobs.Where(job => job.State is "Paused" or "Failed").Select(job => job.Id)); return Task.CompletedTask; }));
        actions.Children.Add(Ui.Button("Pause selected", () => { foreach (var job in Selected()) _engine.Pause(job.Id); return Task.CompletedTask; }));
        actions.Children.Add(Ui.Button("Remove selected", () => { foreach (var job in Selected()) { _engine.Remove(job.Id); _jobs.Remove(job); } return Task.CompletedTask; }));
        actions.Children.Add(Ui.Button("Clear completed", () => { foreach (var job in _jobs.Where(job => job.State == "Completed").ToArray()) { _engine.Remove(job.Id); _jobs.Remove(job); } return Task.CompletedTask; }));
        DockPanel.SetDock(actions, Dock.Top); queuePanel.Children.Add(actions);
        _queue.ItemsSource = _jobs;
        _queue.ItemTemplate = new FuncDataTemplate<Job>((_, _) => new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(Job.Line)) });
        queuePanel.Children.Add(_queue); Grid.SetRow(queuePanel, 2); body.Children.Add(queuePanel);
        var logSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(logSplitter, 3); body.Children.Add(logSplitter);
        Grid.SetRow(_log, 4); body.Children.Add(_log); root.Children.Add(body); Content = root;
        if (Environment.GetEnvironmentVariable("FLUXFTP_RENDER_PATH") is { Length: > 0 } renderPath)
            Opened += (_, _) => DispatcherTimer.RunOnce(() =>
            {
                using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                image.Render(this); image.Save(renderPath);
                Console.WriteLine("PASS: native Linux window rendered to " + renderPath); Close();
            }, TimeSpan.FromSeconds(3));
        Loaded += async (_, _) =>
        {
            try { await ConfigureApi(_configuration.Load().Options.Api ?? new(), false); }
            catch (Exception ex) { Log("API startup failed: " + ex.Message); }
        };
        Closing += async (_, e) =>
        {
            if (_closing) return;
            e.Cancel = true; _closing = true;
            await _apiGate.WaitAsync();
            try { if (_api is not null) { await _api.DisposeAsync(); _api = null; } }
            finally { _apiGate.Release(); }
            await _engine.DisposeAsync(); await _left.Disconnect(); await _right.Disconnect(); Close();
        };
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
        var options = _configuration.Load().Options;
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
    private void Log(string message) => Dispatcher.UIThread.Post(() =>
    {
        if (Environment.GetEnvironmentVariable("FLUXFTP_API_TEST_LOG") == "1") Console.WriteLine(message);
        _log.Text = (_log.Text ?? "") + $"{DateTime.Now:yyyy.MM.dd-HH:mm:ss} {message}\n";
        if (_log.Text.Length > 60000) _log.Text = _log.Text[^40000..];
        _log.CaretIndex = _log.Text.Length;
    });
    private void UpdateJobs()
    {
        foreach (var status in _engine.Snapshot())
        {
            var job = _jobs.FirstOrDefault(job => job.Id == status.Item.Id); if (job is null) continue;
            job.State = status.State.ToString(); job.Error = status.Error ?? "";
            if (status.State != TransferWorkState.Running) job.Speed = 0;
            job.Refresh();
        }
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
