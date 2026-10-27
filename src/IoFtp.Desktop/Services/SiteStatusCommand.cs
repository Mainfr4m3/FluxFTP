namespace IoFtp.Desktop.Services;

internal static class SiteStatusCommand
{
    public const string DefaultCommand = "SITE WKUP 50";
    public const int DefaultIntervalMinutes = 60;

    public static void Validate(string command, int intervalMinutes)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 1024 || command.Any(char.IsControl))
            throw new ArgumentException("Status command must contain one non-empty command on a single line (maximum 1024 characters).");
        if (intervalMinutes is < 1 or > 10080)
            throw new ArgumentException("Status command interval must be between 1 and 10080 minutes.");
    }
}
