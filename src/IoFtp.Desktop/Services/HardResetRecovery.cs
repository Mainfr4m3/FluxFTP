using IoFtp.Core.Transport;

namespace IoFtp.Desktop.Services;

internal sealed record HardResetPolicy(int CooldownSeconds = 30, int Retries = 2, int LookbackMinutes = 10)
{
    public TimeSpan Cooldown => TimeSpan.FromSeconds(Math.Clamp(CooldownSeconds, 0, 3600));
    public int MaxRetries => Math.Clamp(Retries, 1, 10);
    public TimeSpan Lookback => TimeSpan.FromMinutes(Math.Clamp(LookbackMinutes, 1, 1440));
}

internal sealed class HardResetRecovery(Func<DateTimeOffset>? clock = null)
{
    private sealed record Failure(DateTimeOffset At, int Attempts, bool Pending);
    private readonly Dictionary<Guid, Failure> _failures = [];
    private DateTimeOffset Now => clock?.Invoke() ?? DateTimeOffset.UtcNow;
    public static bool Recoverable(string? error)
    {
        if (string.IsNullOrWhiteSpace(error) || TransferRejection.Category(error) is not null) return false;
        if (new[] { "CRC", "verification", "0SIZE", "530 ", "550 ", "553 ", "Permission denied" }.Any(word => error.Contains(word, StringComparison.OrdinalIgnoreCase))) return false;
        return new[] { "OpenSSL", "socket", "connection", "timeout", "timed out", "time limit", "canceled", "cancelled", "425 ", "426 ", "421 " }
            .Any(word => error.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
    public bool Capture(Guid id, string? error)
    {
        if (!Recoverable(error)) { _failures.Remove(id); return false; }
        var attempts = _failures.TryGetValue(id, out var previous) ? previous.Attempts : 0;
        if (_failures.Count >= 1024 && !_failures.ContainsKey(id)) return false;
        _failures[id] = new(Now, attempts, true); return true;
    }
    public bool Eligible(Guid id, HardResetPolicy policy) => _failures.TryGetValue(id, out var failure) && failure.Pending &&
        Now - failure.At <= policy.Lookback && Now - failure.At >= policy.Cooldown && failure.Attempts < policy.MaxRetries;
    public bool Claim(Guid id, HardResetPolicy policy)
    {
        if (!Eligible(id, policy)) return false;
        var failure = _failures[id]; _failures[id] = failure with { Attempts = failure.Attempts + 1, Pending = false }; return true;
    }
    public bool Expired(Guid id, HardResetPolicy policy) => !_failures.TryGetValue(id, out var failure) || Now - failure.At > policy.Lookback;
    public void Remove(Guid id) => _failures.Remove(id);
}
