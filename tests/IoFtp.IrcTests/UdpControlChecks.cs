using System.Net;
using System.Net.Sockets;
using System.Text;
using IoFtp.Desktop.Services;
internal static class UdpControlChecks
{
    public static async Task Run()
    {
        int port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        var count = 0;
        await using var server = new UdpControlServer(port, true, "test-key", command => { count++; return Task.FromResult("OK " + command); });
        using var client = new UdpClient();
        async Task<string> Send(string input)
        {
            await client.SendAsync(Encoding.UTF8.GetBytes(input), new IPEndPoint(IPAddress.Loopback, port));
            using var timeout = new CancellationTokenSource(3000);
            return Encoding.UTF8.GetString((await client.ReceiveAsync(timeout.Token)).Buffer);
        }
        if (!(await Send("wrong race TV R A,B")).StartsWith("ERROR") || count != 0) throw new Exception("UDP unauthorized request dispatched");
        if (!(await Send("test-key race TV R A;B,A;C")).StartsWith("OK race") || count != 1) throw new Exception("UDP batch payload lost");
        if (!(await Send("test-key " + new string('x', 8200))).StartsWith("ERROR") || count != 1) throw new Exception("Oversized UDP accepted");
        Console.WriteLine("PASS: localhost UDP response, authentication, batch payload and oversized rejection.");
    }
}
