using System.Net;
using System.Net.Sockets;
using System.Text;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;

internal static class UploadTimestampChecks
{
    public static async Task Run()
    {
        foreach (var replyCode in new[] { 213, 500, 550 })
            await Check(replyCode);
        Console.WriteLine("PASS: upload timestamp UTC conversion, full paths, unsupported/denied metadata and control-session reuse.");
    }

    private static async Task Check(int replyCode)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var commands = new List<string>();
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = peer.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 Timestamp test");
            while (await reader.ReadLineAsync(timeout.Token) is { } command)
            {
                commands.Add(command);
                var response = command.Split(' ', 2)[0] switch
                {
                    "USER" => "331 Password required",
                    "PASS" => "230 Logged in",
                    "FEAT" => "211-Features\r\n MFMT\r\n211 End",
                    "MFMT" => $"{replyCode} Timestamp result",
                    "QUIT" => "221 Bye",
                    _ => "200 OK"
                };
                await writer.WriteLineAsync(response);
                if (command == "QUIT") break;
            }
        });
        await using var session = new FtpRemoteSession();
        await session.ConnectAsync(new ConnectionProfile(Guid.NewGuid(), "test", "127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, "test", TransferProtocol.Ftp, "password"), timeout.Token);
        var timestamp = new DateTimeOffset(2023, 7, 15, 18, 34, 56, TimeSpan.FromHours(2));
        var result = await session.SetModifiedTimeAsync("/folder with spaces/source.txt", timestamp, timeout.Token);
        if (result.StatusCode != replyCode)
            throw new Exception("Timestamp rejection/success was not returned to the caller.");
        // A metadata rejection must leave the connection available; it must not
        // trigger a retry of the already uploaded payload.
        var noop = await session.ExecuteCommandAsync("NOOP", timeout.Token);
        if (noop.StatusCode != 200) throw new Exception("Metadata rejection broke the control session.");
        await session.DisconnectAsync(timeout.Token);
        await server;
        if (!commands.Where(command => command.StartsWith("MFMT ")).SequenceEqual(
                new[] { "MFMT 20230715163456 /folder with spaces/source.txt" }))
            throw new Exception("Timestamp lost UTC conversion or the full destination pathname.");
    }
}
