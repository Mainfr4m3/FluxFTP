using System.Text.Json;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;

internal static class HardResetChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var recovery = new HardResetRecovery(() => now);
        var policy = new HardResetPolicy(30, 2, 10);
        var id = Guid.NewGuid();
        if (!recovery.Capture(id, "OpenSSL write failed: 80002745") || recovery.Eligible(id, policy)) throw new Exception("Cooldown not enforced");
        now += TimeSpan.FromSeconds(30);
        if (!recovery.Claim(id, policy) || recovery.Claim(id, policy)) throw new Exception("Duplicate or missing retry claim");
        recovery.Capture(id, "The operation was canceled.");
        if (recovery.Eligible(id, policy)) throw new Exception("Cooldown must restart after failure");
        now += TimeSpan.FromSeconds(30);
        if (!recovery.Claim(id, policy)) throw new Exception("Second configured retry unavailable");
        recovery.Capture(id, "Connection timed out"); now += TimeSpan.FromSeconds(30);
        if (recovery.Claim(id, policy)) throw new Exception("Retry limit exceeded");
        var old = Guid.NewGuid(); recovery.Capture(old, "Socket connection lost");
        now += TimeSpan.FromMinutes(11);
        if (!recovery.Expired(old, policy) || recovery.Eligible(old, policy)) throw new Exception("Lookback not enforced");
        foreach (var message in new[] { "553 File exists", "553 File is being uploaded by user", "226 CRC-Check: Failed!", "Server verification rejected the transfer", "530 Login failed", "550 Permission denied", "NUKE / REASON marker stopped the race" })
            if (recovery.Capture(Guid.NewGuid(), message)) throw new Exception("Permanent rejection eligible: " + message);
        recovery.Remove(id);
        if (!recovery.Expired(id, policy)) throw new Exception("Removed job retained retry state");
        var defaults = JsonSerializer.Deserialize<GlobalSettings>("{}")!;
        if (defaults != new GlobalSettings()) throw new Exception("Missing settings must use constructor defaults");
        if (defaults.TransferJobHistory != 2000 || defaults.TransferHistory != 2000 || defaults.LogBufferHistory != 4096 ||
            defaults.PreparedJobExpirationSeconds != 45 || defaults.StarterTimeoutSeconds != 15 || defaults.MaxTransferRuntimeMinutes != 15 ||
            defaults.MaxActiveSpreadJobs != 150 || defaults.VisionaryConcurrentScheduling != 150 || defaults.VisionaryRecheckSeconds != 1)
            throw new Exception("Visi bridge defaults do not match the configured baseline");
        if (defaults.AutoHardReset || defaults.HardResetCooldownSeconds != 120 || defaults.HardResetRetries != 2 || defaults.HardResetLookbackMinutes != 5)
            throw new Exception("Recovery defaults do not match the configured baseline");
        var saved = defaults with { TransferHistory = 10000, StarterTimeoutSeconds = 300, MaxActiveSpreadJobs = 32,
            VisionaryConcurrentScheduling = 32, VisionaryRecheckSeconds = null, AutoHardReset = false,
            HardResetCooldownSeconds = 30, HardResetRetries = 2, HardResetLookbackMinutes = 10 };
        if (JsonSerializer.Deserialize<GlobalSettings>(JsonSerializer.Serialize(saved)) != saved)
            throw new Exception("Explicit saved preferences must remain unchanged by new defaults");
        var settings = defaults with { VisionaryRecheckSeconds = 1, VisionaryConcurrentScheduling = 64, AutoHardReset = true, HardResetCooldownSeconds = 45, HardResetRetries = 3, HardResetLookbackMinutes = 15 };
        if (JsonSerializer.Deserialize<GlobalSettings>(JsonSerializer.Serialize(settings)) != settings) throw new Exception("Recovery preferences not persisted");
        Console.WriteLine("PASS: hard reset cooldown, bounded retries, duplicate claims, lookback, permanent rejection exclusions and preference migration.");
    }
}
