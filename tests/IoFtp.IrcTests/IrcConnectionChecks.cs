using System.IO;
using System.Net;
using System.Net.Sockets;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;

internal static class IrcConnectionChecks
{
    public static async Task Run()
    {
        foreach (var znc in new[] { false, true })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var settings = new IrcSettings(Host: "127.0.0.1", Port: ((IPEndPoint)listener.LocalEndpoint).Port,
                UseTls: false, Password: "test-secret", UseZnc: znc, ZncUsername: "user", ZncNetwork: "net");
            await using var service = new IrcService(new GlobalSettings(Irc: settings), message =>
            { if (message.Contains("Retrying")) failure.TrySetResult(message); });
            using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var reader = new StreamReader(client.GetStream());
            using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true, NewLine = "\r\n" };
            var first = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (first != (znc ? "PASS :user/net:test-secret" : "PASS :test-secret")) throw new Exception("IRC PASS format/order failed");
            await writer.WriteLineAsync(":server 464 FluxFTP :Password mismatch test-secret");
            var logged = await failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!logged.Contains("password rejected (464)") || logged.Contains("test-secret")) throw new Exception("IRC auth failure diagnostic failed");
        }
        Console.WriteLine("PASS: IRC direct/ZNC PASS and rejection integration checks.");
    }
}
