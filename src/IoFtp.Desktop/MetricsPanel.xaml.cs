using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IoFtp.Desktop;

public partial class MetricsPanel : UserControl
{
    private readonly Func<MetricsSnapshot> _snapshot;
    private readonly Action _transferJobs, _raceLog, _spreadJobs, _hide;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    internal MetricsPanel(Func<MetricsSnapshot> snapshot, Action transferJobs, Action raceLog, Action spreadJobs, Action hide)
    {
        _transferJobs = transferJobs; _raceLog = raceLog; _spreadJobs = spreadJobs; _hide = hide;
        InitializeComponent(); _snapshot = snapshot; _timer.Tick += (_, _) => RefreshMetrics();
        Loaded += (_, _) => { RefreshMetrics(); _timer.Start(); }; Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible) { RefreshMetrics(); _timer.Start(); } else _timer.Stop(); };
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshMetrics();
    private void TransferJobs_Click(object sender, RoutedEventArgs e) => _transferJobs();
    private void RaceLog_Click(object sender, RoutedEventArgs e) => _raceLog();
    private void SpreadJobs_Click(object sender, RoutedEventArgs e) => _spreadJobs();
    private void Close_Click(object sender, RoutedEventArgs e) => _hide();
    private void RefreshMetrics()
    {
        var value = _snapshot(); SitesText.Text = $"Configured sites: {value.ConfiguredSites} · Browsing connected: {value.ConnectedSites}"; ActiveText.Text = $"{value.ActiveJobs}";
        UpdatedText.Text = "Current session · updated " + DateTime.Now.ToString("HH:mm:ss");
        if (value.Dashboard is { } dashboard)
        {
            BytesText.Text = dashboard.Transferred; FilesText.Text = dashboard.Files.ToString("N0");
            SpeedText.Text = dashboard.FxpRate; PoolText.Text = dashboard.Pool.ToString(); UptimeText.Text = dashboard.Uptime;
            PoolDetailText.Text = $"Pool reuse hits: {dashboard.Hits:N0}";
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            ProcessText.Text = $"Memory: {process.WorkingSet64 / 1048576d:0} MB · Threads: {process.Threads.Count}";
            _traffic = dashboard.Traffic; DrawTraffic();
        }
        MetricsList.ItemsSource = value.Rows;
    }
    private IReadOnlyList<(long Minute, long Bytes)> _traffic = [];
    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTraffic();
    private void DrawTraffic()
    {
        TrafficChart.Children.Clear();
        var width = TrafficChart.ActualWidth; var height = TrafficChart.Height;
        if (width <= 0) return;
        var maximum = Math.Max(1, _traffic.Count == 0 ? 0 : _traffic.Max(point => point.Bytes));
        ChartScaleText.Text = $"Peak {maximum / 1_000_000d:0.##} MB / min";
        ChartStartText.Text = _traffic.Count > 0 ? $"Session minute {_traffic[0].Minute}" : "Waiting for completed transfers";
        var border = (Brush)FindResource("BorderBrush"); var accent = (Brush)FindResource("AccentBrush");
        for (var i = 0; i <= 3; i++) TrafficChart.Children.Add(new Line { X1 = 0, X2 = width, Y1 = height * i / 3, Y2 = height * i / 3, Stroke = border, StrokeThickness = 1 });
        var step = width / 60;
        for (var i = 0; i < _traffic.Count; i++)
        {
            var point = _traffic[i]; var barHeight = height * point.Bytes / maximum;
            var bar = new Rectangle { Width = Math.Max(1, step - 3), Height = Math.Max(0, barHeight), Fill = accent,
                RadiusX = 2, RadiusY = 2, ToolTip = $"Session minute {point.Minute}: {point.Bytes / 1_000_000d:0.##} MB" };
            Canvas.SetLeft(bar, i * step); Canvas.SetTop(bar, height - barHeight); TrafficChart.Children.Add(bar);
        }
    }
}
