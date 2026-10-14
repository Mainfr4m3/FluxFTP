using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace IoFtp.Desktop.Services;

internal sealed class UdpControlServer : IAsyncDisposable
{
    private readonly UdpClient _client;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    public UdpControlServer(int port, bool localOnly, string password, Func<string, Task<string>> dispatch)
    {
        _client = new UdpClient(new IPEndPoint(localOnly ? IPAddress.Loopback : IPAddress.Any, port));
        _loop = Listen(password, dispatch);
    }
    private async Task Listen(string password, Func<string, Task<string>> dispatch)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var packet = await _client.ReceiveAsync(_stop.Token);
                string response;
                try
                {
                    var text = new UTF8Encoding(false, true).GetString(packet.Buffer).TrimEnd('\r', '\n');
                    var separator = text.IndexOf(' ');
                    if (packet.Buffer.Length > 8192 || separator < 1 || string.IsNullOrEmpty(password) ||
                        !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(text[..separator]), Encoding.UTF8.GetBytes(password)))
                        response = "ERROR Unauthorized or invalid UDP request";
                    else response = await dispatch(text[(separator + 1)..]);
                }
                catch (Exception ex) { response = "ERROR " + ex.Message.Replace('\r', ' ').Replace('\n', ' '); }
                var bytes = Encoding.UTF8.GetBytes(response + "\n");
                if (bytes.Length > 60000) bytes = Encoding.UTF8.GetBytes("ERROR Response too large; use HTTPS API\n");
                await _client.SendAsync(bytes, packet.RemoteEndPoint, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync() { _stop.Cancel(); await _loop; _client.Dispose(); _stop.Dispose(); }
}
