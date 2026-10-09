using System.Windows;
using System.Windows.Threading;

namespace IoFtp.Desktop;

public partial class MetricsWindow : Window
{
    private readonly Func<MetricsSnapshot> _snapshot;
    private readonly Action _transferJobs, _raceLog, _spreadJobs;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    internal MetricsWindow(Func<MetricsSnapshot> snapshot, Action transferJobs, Action raceLog, Action spreadJobs)
    {
        _transferJobs = transferJobs; _raceLog = raceLog; _spreadJobs = spreadJobs;
        InitializeComponent(); _snapshot = snapshot; _timer.Tick += (_, _) => RefreshMetrics();
        Loaded += (_, _) => { RefreshMetrics(); _timer.Start(); }; Closed += (_, _) => _timer.Stop();
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshMetrics();
    private void TransferJobs_Click(object sender, RoutedEventArgs e) => _transferJobs();
    private void RaceLog_Click(object sender, RoutedEventArgs e) => _raceLog();
    private void SpreadJobs_Click(object sender, RoutedEventArgs e) => _spreadJobs();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void RefreshMetrics()
    {
        var value = _snapshot(); SitesText.Text = $"{value.ConnectedSites}/{value.ConfiguredSites}"; ActiveText.Text = $"{value.ActiveJobs}";
        SpeedText.Text = value.TotalSpeed; BytesText.Text = value.Transferred; UpdatedText.Text = DateTime.Now.ToString("HH:mm:ss");
        MetricsList.ItemsSource = value.Rows;
    }
}

internal sealed record MetricRow(string Name, string Value, string Detail);
internal sealed record MetricsSnapshot(int ConnectedSites, int ConfiguredSites, int ActiveJobs, string TotalSpeed, string Transferred, IReadOnlyList<MetricRow> Rows);
