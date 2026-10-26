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
    private readonly LinuxConfiguration _configuration = new();
    public MainWindow()
    {
        Title = "FluxFTP Linux - 0.1.0 preview 2"; Width = 1280; Height = 850; MinWidth = 950; MinHeight = 650;
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
        toolbar.Children.Add(Ui.Button("About", async () => await new Window { Title = "About FluxFTP Linux", Width = 520, Height = 220, Content = new TextBlock { Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap, Text = "FluxFTP Linux 0.1.0 preview 2\nSame FTP/FTPS/SFTP and transfer engine as FluxFTP Windows.\nWindows backup import, saved sites and sections are supported. Bridge, full settings and recursive folder transfers are not ported yet." } }.ShowDialog(this)));
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
        Closing += async (_, e) =>
        {
            if (_closing) return;
            e.Cancel = true; _closing = true;
            await _engine.DisposeAsync(); await _left.Disconnect(); await _right.Disconnect(); Close();
        };
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
