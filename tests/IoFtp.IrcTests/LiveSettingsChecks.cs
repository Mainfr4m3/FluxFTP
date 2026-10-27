using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;

internal static class LiveSettingsChecks
{
    public static async Task Run()
    {
        var running = new GlobalSettings(EnableHttpsApi: true, EnableVisionaryBridge: true);
        var changed = running with { StatusCommand = "SITE WKUP 10", StatusCommandIntervalMinutes = 5,
            VisionaryConcurrentScheduling = 200, VisionaryRecheckSeconds = 3, MaxActiveSpreadJobs = 100,
            MaxLocalDownloadSlots = 3, ProxyHost = "proxy.example", Theme = new() };
        if (SettingsLiveUpdate.ApiListenerChanged(running, changed))
            throw new Exception("Ordinary settings must not require an API/bridge restart.");
        foreach (var listener in new[] { running with { HttpsApiPort = 55500 }, running with { ApiLocalhostOnly = false },
            running with { ApiPassword = "changed" }, running with { EnableHttpsApi = false }, running with { EnableVisionaryBridge = false } })
            if (!SettingsLiveUpdate.ApiListenerChanged(running, listener)) throw new Exception("Listener change not reported.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var slots = new RaceWatchSlots(1);
        var first = await slots.EnterAsync(timeout.Token);
        var waiting = slots.EnterAsync(timeout.Token);
        if (waiting.IsCompleted) throw new Exception("Initial scheduling capacity exceeded.");
        slots.Configure(2);
        var second = await waiting;
        slots.Configure(1);
        var third = slots.EnterAsync(timeout.Token);
        first.Dispose();
        if (third.IsCompleted) throw new Exception("Lowering the limit admitted work while still full.");
        second.Dispose();
        using var next = await third;
        first.Dispose(); // A lease may be disposed twice without changing capacity.
        using var cancel = new CancellationTokenSource();
        var cancelled = slots.EnterAsync(cancel.Token);
        cancel.Cancel();
        try { await cancelled; throw new Exception("Cancelled waiter admitted."); }
        catch (OperationCanceledException) { }
        // Closing the service wakes queued scheduling steps instead of leaving them hung.
        var disposed = slots.EnterAsync(timeout.Token);
        slots.Dispose();
        try { await disposed; throw new Exception("Disposed scheduler admitted work."); }
        catch (ObjectDisposedException) { }
        Console.WriteLine("PASS: live settings classification; resize scheduling up/down without cancelling active leases; waiter cancellation and disposal.");
    }
}
