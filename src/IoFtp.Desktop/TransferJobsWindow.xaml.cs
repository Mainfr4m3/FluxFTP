using System.Windows;
using IoFtp.Desktop.Models;
namespace IoFtp.Desktop;
public partial class TransferJobsWindow : Window
{
    internal TransferJobsWindow(Func<IReadOnlyList<TransferJobInfo>> getJobs, Action<Guid> removeJob, Action clearJobs, Func<Guid?, int> hardReset)
    {
        InitializeComponent();
        JobsHost.Content = new TransferJobsPanel(getJobs, removeJob, clearJobs, hardReset);
    }
}
