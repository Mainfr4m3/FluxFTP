namespace IoFtp.Desktop.Services;

// Per control session: keep a SITE command and its multiline reply together.
internal sealed class SiteCommandLogRouter
{
    private bool _siteReply;
    public bool Route(string message)
    {
        if (message.StartsWith("> ", StringComparison.Ordinal))
        {
            var command = message[2..].TrimStart();
            _siteReply = command.Equals("SITE", StringComparison.OrdinalIgnoreCase) ||
                command.StartsWith("SITE ", StringComparison.OrdinalIgnoreCase);
        }
        return _siteReply;
    }
}
