using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal sealed class IrcManager : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly IrcService _primary;
    private readonly Task _monitor;
    public IrcManager(GlobalSettings settings, Action<string> log, Func<string, Func<bool>, Task<string>> setupCommand,
        IrcRoutingStore routing, IrcCatcher catcher)
    {
        _primary = new(settings, log, setupCommand: setupCommand, routingStore: routing, catcher: catcher);
        _monitor = Task.Run(async () =>
        {
            var active = new Dictionary<string, (IrcNetwork Config, IrcService Service)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        var networks = routing.Load().Networks.Where(n => !n.Name.Equals(settings.Irc!.NetworkName, StringComparison.OrdinalIgnoreCase)).ToArray();
                        foreach (var name in active.Keys.ToArray())
                            if (!networks.Any(n => n == active[name].Config))
                            { await active[name].Service.DisposeAsync(); active.Remove(name); }
                        foreach (var network in networks)
                            if (!active.ContainsKey(network.Name))
                            {
                                // Never reuse primary account links or FTP authority on another IRC network.
                                var secondary = settings with { Irc = new(Enabled: true, Host: network.Host, Port: network.Port,
                                    UseTls: network.Tls, Nick: network.Nick, Password: network.Password,
                                    AllowPrivateLogin: false, NetworkName: network.Name) };
                                active.Add(network.Name, (network, new(secondary, log, routingStore: routing, catcher: catcher, controlEnabled: false)));
                            }
                    }
                    catch (Exception ex) { log($"IRC network configuration unavailable ({ex.GetType().Name})."); }
                    await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            finally { foreach (var item in active.Values) await item.Service.DisposeAsync(); }
        });
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _primary.DisposeAsync();
        await _monitor;
        _stop.Dispose();
    }
}
