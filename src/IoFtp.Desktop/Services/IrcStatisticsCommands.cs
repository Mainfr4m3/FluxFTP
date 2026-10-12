namespace IoFtp.Desktop.Services;

internal static class IrcStatisticsCommands
{
    public const string Help = "!flux stats | !flux stats total | !flux stats pool";
    public static string Execute(string command, Func<string, string> snapshot)
    {
        var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (args.Length is < 2 or > 3 || !args[0].Equals("!flux", StringComparison.OrdinalIgnoreCase) ||
            !args[1].Equals("stats", StringComparison.OrdinalIgnoreCase)) return "ERROR: " + Help;
        if (args.Length == 2) return snapshot("session");
        var kind = args[2].ToLowerInvariant();
        return kind is "total" or "pool" ? snapshot(kind) : "ERROR: " + Help;
    }
}
