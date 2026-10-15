namespace IoFtp.Desktop.Services;

internal static class VisionaryLogVisibility
{
    public static bool Show(string message, bool debug)
    {
        var text = message.TrimStart();
        if (text.StartsWith("SERVER STATS", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("SERVER DETAILS", StringComparison.OrdinalIgnoreCase)) return debug;
        if (debug || !text.StartsWith("VISIONARY", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("VISIONARY TIMING", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("VISIONARY queued ", StringComparison.OrdinalIgnoreCase)) return true;
        return new[] { "error", "failed", "failure", "rejected", "blocked", "warning", "stopped", "timeout", "timed out", "NUKE" }
            .Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
