using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;

namespace IoFtp.Desktop.Services;

internal sealed class IrcConnectionError(string message) : IOException(message)
{
    public static string Describe(Exception error) => error switch
    {
        IrcConnectionError known => known.Message,
        AuthenticationException => "TLS handshake or certificate validation failed. Check server hostname, TLS setting and port.",
        SocketException socket => $"Network connection failed ({socket.SocketErrorCode}). Check hostname and port.",
        IOException { InnerException: SocketException socket } => $"Connection interrupted ({socket.SocketErrorCode}). Check the server port and TLS setting.",
        IOException => "Server closed the connection or the connection could not be read. Check TLS/port and the server connection log.",
        OperationCanceledException or TimeoutException => "Connection or registration timed out. Check server, port and TLS setting.",
        _ => $"Connection failed ({error.GetType().Name}). Check IRC configuration."
    };
    public static string? RegistrationReply(string command, string text) => command switch
    {
        "431" => "Server requires a nickname (431).",
        "432" => "Server rejected the nickname (432). Change Nickname in Global Settings.",
        "433" or "436" => $"Nickname is already in use or collided ({command}). Choose another Nickname.",
        "464" => "Server password rejected (464). Check Server password / ZNC password and whether ZNC mode is enabled.",
        "465" => "Server denied access: banned connection (465). Contact the IRC administrator.",
        "466" => "Server is refusing this connection (466). Contact the IRC administrator.",
        "ERROR" when text.Contains("password", StringComparison.OrdinalIgnoreCase) => "Server closed the connection: password/authentication error. Check the server password and ZNC mode.",
        "ERROR" when text.Contains("throttl", StringComparison.OrdinalIgnoreCase) || text.Contains("too many", StringComparison.OrdinalIgnoreCase) => "Server closed the connection: connection limit or throttling. Close duplicate clients and wait before retrying.",
        "ERROR" when text.Contains("banned", StringComparison.OrdinalIgnoreCase) || text.Contains("k-line", StringComparison.OrdinalIgnoreCase) => "Server closed the connection: access denied/banned.",
        "ERROR" when text.Contains("TLS", StringComparison.OrdinalIgnoreCase) || text.Contains("SSL", StringComparison.OrdinalIgnoreCase) => "Server closed the connection: TLS/SSL problem. Check TLS setting and port.",
        "ERROR" => "Server sent ERROR and closed the connection. Check the IRC server log for the exact reason.",
        _ => null
    };
}
