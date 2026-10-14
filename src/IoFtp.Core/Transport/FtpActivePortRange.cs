using System.Net;
using System.Net.Sockets;
namespace IoFtp.Core.Transport;
public static class FtpActivePortRange
{
    public static TcpListener Start(IPAddress address, int min, int max)
    {
        if (min == 0 && max == 0) { var automatic = new TcpListener(address, 0); automatic.Start(); return automatic; }
        if (min < 1 || max > 65535 || max < min) throw new ArgumentException("Invalid active port range.");
        var length = max - min + 1;
        var start = Random.Shared.Next(length);
        SocketException? last = null;
        for (var i = 0; i < length; i++)
        {
            var listener = new TcpListener(address, min + (start + i) % length);
            try { listener.Start(); return listener; }
            catch (SocketException exception) when (exception.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            { listener.Stop(); last = exception; }
            catch { listener.Stop(); throw; }
        }
        throw new IOException($"No available local active FTP port in {min}-{max}. Check port use, permissions and Global Settings.", last);
    }
}
