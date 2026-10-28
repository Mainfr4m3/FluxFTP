using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;

namespace IoFtp.Desktop;

public partial class MainWindow
{
    private readonly ConcurrentDictionary<Guid, int> _commandWaiters = new();
    private readonly ConcurrentDictionary<Guid, byte> _manuallyDisconnectedSites = new();

    private async Task<FtpRemoteSession> RentCommandWorkerAsync(ConnectionProfile profile, CancellationToken token, bool fresh = false)
    {
        _commandWaiters.AddOrUpdate(profile.Id, 1, (_, value) => value + 1);
        try { return await RentWorkerAsync(profile, token, forceFresh: fresh, controlCommand: true); }
        finally { _commandWaiters.AddOrUpdate(profile.Id, 0, (_, value) => Math.Max(0, value - 1)); }
    }

    internal async Task TestSitesAsync(IEnumerable<ConnectionProfile> profiles, Action<string> report, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _racePoolStop.Token);
        foreach (var saved in profiles)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var profile = ApplyGlobalProxy(saved);
            FtpRemoteSession? session = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                report($"{profile.Name}: testing login / {_settings.StatusCommand} / logout…");
                session = await RentCommandWorkerAsync(profile, timeout.Token, fresh: true);
                var reply = await session.ExecuteCommandAsync(_settings.StatusCommand, timeout.Token);
                report($"{profile.Name}: LOGIN OK; {reply.StatusCode} {reply.Message.Trim()}");
                await session.ExecuteCommandAsync("QUIT", timeout.Token);
                report($"{profile.Name}: logged out.");
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { report($"{profile.Name}: timed out waiting for a slot or server response."); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { report($"{profile.Name}: FAILED — {FriendlyMessage(error)}"); }
            finally { if (session is not null) await DisposeTransferWorkerAsync(session); }
        }
    }

    private void DisconnectSites_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        var all = new MenuItem { Header = "All sites" };
        all.Click += async (_, _) => await DisconnectSitesWithReportAsync(new ProfileStore().Load());
        menu.Items.Add(all);
        menu.Items.Add(new Separator());
        foreach (var profile in new ProfileStore().Load().OrderBy(p => p.Name))
        {
            var item = new MenuItem { Header = profile.Name };
            item.Click += async (_, _) => await DisconnectSitesWithReportAsync([profile]);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private async Task DisconnectSitesWithReportAsync(IEnumerable<ConnectionProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            _manuallyDisconnectedSites[profile.Id] = 1;
            _raceReadySites.Remove(profile.Id);
            _engine.DisconnectSite(profile.Id);
        }
        foreach (var profile in profiles)
        {
            try
            {
                if (_leftProfile?.Id == profile.Id) await DisconnectLeftAsync();
                if (_rightProfile?.Id == profile.Id) await DisconnectRightAsync();
                await DisposePooledWorkersAsync(profile.Id);
                foreach (var worker in _sessionWorkers.Where(pair => pair.Value.Id == profile.Id).Select(pair => pair.Key).ToArray())
                    await DisposeTransferWorkerAsync(worker);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await _raceListings.DisconnectAsync(profile.Id, timeout.Token);
                AppendFtpLog($"{Environment.NewLine}DISCONNECTED [{profile.Name}] — queued work paused; connect the site again to allow new logins.");
            }
            catch (Exception error) { AppendFtpLog($"{Environment.NewLine}DISCONNECT [{profile.Name}]: {FriendlyMessage(error)}"); }
        }
        UpdateConnectionButtons();
    }
}
