using System.Windows;
using System.Windows.Threading;
using IoFtp.Desktop.Models;
using System.Windows.Controls;
using System.Windows.Input;

namespace IoFtp.Desktop;

public partial class TransferJobsWindow : Window
{
    private readonly Func<IReadOnlyList<TransferJobInfo>> _getJobs;
    private readonly Action<Guid> _removeJob;
    private readonly Action _clearJobs;
    private readonly Func<Guid?, int> _hardReset;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    internal TransferJobsWindow(Func<IReadOnlyList<TransferJobInfo>> getJobs, Action<Guid> removeJob, Action clearJobs, Func<Guid?, int> hardReset)
    {
        InitializeComponent();
        _getJobs = getJobs;
        _removeJob = removeJob;
        _clearJobs = clearJobs;
        _hardReset = hardReset;
        _timer.Tick += (_, _) => RefreshJobs();
        Loaded += (_, _) => { RefreshJobs(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshJobs();
    private void HardResetSelected_Click(object sender, RoutedEventArgs e)
    {
        if (JobsList.SelectedItem is TransferJobInfo job) { var count = _hardReset(job.Id); RefreshJobs(); CountersText.Text += $"   Hard reset queued: {count}"; }
    }
    private void HardResetEligible_Click(object sender, RoutedEventArgs e)
    {
        var count = _hardReset(null); RefreshJobs(); CountersText.Text += $"   Hard reset queued: {count}";
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void RemoveJob_Click(object sender, RoutedEventArgs e)
    {
        if (JobsList.SelectedItem is TransferJobInfo job) { _removeJob(job.Id); RefreshJobs(); }
    }
    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Remove all transfer jobs? Active transfers will be stopped.", "Transfer Jobs",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        { _clearJobs(); RefreshJobs(); }
    }
    private void JobsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(JobsList, e.OriginalSource as DependencyObject) is ListViewItem item) item.IsSelected = true;
    }

    private void RefreshJobs()
    {
        var jobs = _getJobs();
        var selected = (JobsList.SelectedItem as TransferJobInfo)?.Id;
        JobsList.ItemsSource = jobs;
        if (selected is not null) JobsList.SelectedItem = jobs.FirstOrDefault(job => job.Id == selected);
        var active = jobs.Count(job => job.State is "Queued" or "Transferring");
        CountersText.Text = $"Active: {active}   Total: {jobs.Count}";
    }
}
