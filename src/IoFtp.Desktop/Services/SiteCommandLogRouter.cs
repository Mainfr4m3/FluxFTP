namespace IoFtp.Desktop.Services;

// Per control session: keep a SITE command and its multiline reply together.
internal sealed class SiteCommandLogRouter
{
    private bool _siteReply;
    public bool Route(string message, string? statusCommand = null)
    {
        if (message.StartsWith("> ", StringComparison.Ordinal))
        {
            var command = message[2..].TrimStart();
            _siteReply = command.Equals("SITE", StringComparison.OrdinalIgnoreCase) ||
                command.StartsWith("SITE ", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(statusCommand) && command.Equals(statusCommand.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        return _siteReply;
    }
}
