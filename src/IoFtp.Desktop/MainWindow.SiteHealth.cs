using System.IO;
using System.Collections.Concurrent;
using System.Windows;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;
namespace IoFtp.Desktop;

public partial class MainWindow
{
    private SiteHealthMonitor _siteHealth = null!;
    private readonly ConcurrentDictionary<Guid, int> _healthConnecting = new();
    private readonly Dictionary<Guid, SiteHealthState> _reportedHealth = [];
    private readonly System.Windows.Threading.DispatcherTimer _siteHealthTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private void InitializeSiteHealth()
    {
        _siteHealth = new((profile, token) => Dispatcher.InvokeAsync(() => ProbeSiteHealthAsync(profile, token)).Task.Unwrap(),
            (id, _) => _engine.SetSiteAvailable(id, _siteHealth.IsAvailable(id)),
            update => Dispatcher.BeginInvoke(() => SiteHealthChanged(update.Id)));
        ConfigureSiteHealth();
        _siteHealthTimer.Tick += async (_, _) => await CheckSiteHealthAsync();
        _siteHealthTimer.Start();
    }
    private void ConfigureSiteHealth()
    {
        _siteHealth.Configure(new(_settings.EnableSiteHealthMonitor, Math.Clamp(_settings.SiteHealthCheckSeconds, 30, 3600),
            Math.Clamp(_settings.SiteHealthRetrySeconds, 15, 3600), Math.Clamp(_settings.SiteHealthFailures, 1, 10), Math.Clamp(_settings.StarterTimeoutSeconds, 1, 30)));
        if (!_settings.EnableSiteHealthMonitor) _reportedHealth.Clear();
    }
    private async Task CheckSiteHealthAsync(bool force = false)
    {
        if (!_settings.EnableSiteHealthMonitor || _workerPoolShuttingDown) return;
        try { await _siteHealth.CheckAsync(new ProfileStore().Load().Select(ApplyGlobalProxy).ToArray(), _racePoolStop.Token, force); }
        catch (OperationCanceledException) when (_racePoolStop.IsCancellationRequested) { }
        catch (Exception error) { AppendBridgeInfo("BRIDGE site monitor: " + FriendlyMessage(error)); }
    }
    private void SiteHealthChanged(Guid id)
    {
        if (!_settings.EnableSiteHealthMonitor || _siteHealth.Snapshot(id) is not { } status) return;
        var before = _reportedHealth.GetValueOrDefault(id);
        _reportedHealth[id] = status.State;
        if (before == status.State) return;
        var text = status.State switch
        {
            SiteHealthState.Offline => $"SITE OFFLINE [{status.Name}] — trade paused; retry in {_settings.SiteHealthRetrySeconds}s.",
            SiteHealthState.LoginFailed => $"LOGIN FAILED [{status.Name}] — trade paused; retry in {Math.Max(600, _settings.SiteHealthRetrySeconds)}s.",
            SiteHealthState.Online => $"SITE ONLINE [{status.Name}]" + (before is SiteHealthState.Offline or SiteHealthState.LoginFailed ? " — trade resumed." : "."),
            _ => null
        };
        if (text is null) return;
        AppendFtpLog(Environment.NewLine + text);
        _bridgeInfo.Write("SITE-HEALTH", text);
    }
    private async Task<FtpRemoteSession> CreateObservedWorkerAsync(ConnectionProfile profile, CancellationToken token)
    {
        if (_manuallyDisconnectedSites.ContainsKey(profile.Id)) throw new IOException($"Site {profile.Name} was manually disconnected.");
        _healthConnecting.AddOrUpdate(profile.Id, 1, (_, count) => count + 1);
        try
        {
            await _siteHealth.WaitForProbeAsync(profile.Id, token);
            if (!_siteHealth.IsAvailable(profile.Id)) throw new IOException($"Connection waiting: site={profile.Name}; automatic health monitoring paused trade.");
            try
            {
                var session = await CreateWorkerAsync(profile, token);
                _siteHealth.ObserveSuccess(profile); return session;
            }
            catch (Exception error) when (!token.IsCancellationRequested) { _siteHealth.ObserveFailure(profile, error); throw; }
        }
        finally { _healthConnecting.AddOrUpdate(profile.Id, 0, (_, count) => Math.Max(0, count - 1)); }
    }
    private async Task<SiteProbeResult> ProbeSiteHealthAsync(ConnectionProfile profile, CancellationToken token)
    {
        if (_manuallyDisconnectedSites.ContainsKey(profile.Id)) return SiteProbeResult.Deferred;
        if (_workerPoolShuttingDown || _leftConnectionBusy || _rightConnectionBusy || _healthConnecting.GetValueOrDefault(profile.Id) > 0) return SiteProbeResult.Deferred;
        try { if (await _raceListings.TryCheckHealthAsync(profile, token) == true) return SiteProbeResult.Online; }
        catch (Exception) when (!token.IsCancellationRequested) { }
        if (_raceListings.HasSession(profile.Id)) return SiteProbeResult.Deferred; // A shared listing connection is currently busy.
        var gate = _sitePoolGates.For(profile.Id);
        if (!await gate.WaitAsync(0, token)) return SiteProbeResult.Deferred;
        try
        {
            if (_healthConnecting.GetValueOrDefault(profile.Id) > 0) return SiteProbeResult.Deferred;
            if (_workerPool.TryGetValue(profile, out var pool))
                while (pool.TryTake(out var pooled))
                {
                    var healthy = false;
                    try
                    {
                        if (pooled.Session.IsConnected) healthy = (await pooled.Session.ExecuteCommandAsync("NOOP", token)).StatusCode is >= 200 and < 300;
                        if (healthy) return SiteProbeResult.Online;
                    }
                    catch (Exception) when (!token.IsCancellationRequested) { }
                    finally { await ReleaseWorkerAsync(profile, pooled.Session, healthy); }
                }
            var browsing = (_leftProfile?.Id == profile.Id && _leftRemoteSession?.IsConnected == true ? 1 : 0) + (_rightProfile?.Id == profile.Id && _remoteSession?.IsConnected == true ? 1 : 0);
            var inUse = _sessionWorkers.Count(pair => pair.Value.Id == profile.Id) + browsing + (_raceListings.HasSession(profile.Id) ? 1 : 0);
            if (inUse >= RaceSlotOptions(profile).MaxSlots) return SiteProbeResult.Deferred;
            // A diagnostic login is never put in the transfer pool. Closing it before
            // resuming trade avoids creating an extra idle/ghost login.
            await using var session = new FtpRemoteSession();
            await session.ConnectAsync(profile, token);
            var reply = await session.ExecuteCommandAsync("NOOP", token);
            if (reply.StatusCode is >= 200 and < 300) return SiteProbeResult.Online;
            throw new FtpCommandException(reply.StatusCode, reply.Message, profile.Name, "NOOP");
        }
        catch (Exception error) when (!token.IsCancellationRequested) { _bridgeInfo.Write("SITE-HEALTH", $"[{profile.Name}] {FriendlyMessage(error)}"); throw; }
        finally { gate.Release(); }
    }
    private string SiteHealthDetails(Guid id)
    {
        if (!_settings.EnableSiteHealthMonitor) return "Automatic site monitoring: disabled (Global Settings > Site monitor).";
        if (_siteHealth.Snapshot(id) is not { } status) return "Automatic site monitoring: waiting for first FTP/FTPS check.";
        return $"Health: {status.State}    Trade: {(status.Available ? "available" : "automatically paused")}\nLast check: {status.CheckedAt?.ToLocalTime().ToString("yyyy.MM.dd-HH:mm:ss") ?? "—"}    Next check: {status.NextCheck.ToLocalTime():HH:mm:ss}    Consecutive failures: {status.Failures}";
    }
    private void OpenSiteHealth_Click(object sender, RoutedEventArgs e)
    {
        SiteStatus_Click(sender, e);
    }
}
