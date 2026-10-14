using IoFtp.Core.Transport;
namespace IoFtp.Desktop.Services;
internal static class ResumeRecovery
{
    public static async Task RunAsync(long offset, bool enabled, Func<long, Task> transfer, Func<Task> reset, Action<string>? log, CancellationToken token)
    {
        try { await transfer(offset); }
        catch (FtpResumeRejectedException exception) when (enabled && offset > 0 && exception.CanOverwrite && !token.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();
            log?.Invoke("Resume rejected; restarting with Overwrite: " + exception.Message);
            await reset();
            token.ThrowIfCancellationRequested();
            await transfer(0); // Only one fallback attempt.
        }
    }
}
