using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IoFtp.Core.Transport;

internal static class OpenSslChecks
{
    public static async Task Run()
    {
        var diagnosticCommand = typeof(FtpRemoteSession).GetMethod("DiagnosticCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var command in new[] { "PASS secret", "USER private-user", "SITE LOGIN user secret", "ACCT secret" })
        {
            var output = (string)diagnosticCommand.Invoke(null, [command])!;
            if (output != command.Split(' ')[0]) throw new Exception("TLS diagnostic exposed credentials.");
        }
        if ((string)diagnosticCommand.Invoke(null, ["RETR /release/file.rar"])! != "RETR /release/file.rar")
            throw new Exception("TLS diagnostic lost transfer command.");
        using var rsa = RSA.Create(2048);
        using var generatedCertificate = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = new X509Certificate2(generatedCertificate.Export(X509ContentType.Pfx));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var server = Task.Run(async () =>
            {
                using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                using var tls = new SslStream(peer.GetStream());
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate=certificate }, timeout.Token);
                var request = new byte[1];
                await tls.ReadExactlyAsync(request, timeout.Token);
                if (request[0]!=7) throw new Exception("OpenSSL write lost data.");
                await Task.Delay(150, timeout.Token);
                await tls.WriteAsync(new byte[] {1,2}, timeout.Token);
                await Task.Delay(150, timeout.Token);
                await tls.WriteAsync(new byte[] {3,4}, timeout.Token);
                await tls.ShutdownAsync();
            }, timeout.Token);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, timeout.Token);
            var transport=client.GetStream();
            client.Client.Blocking=false;
            var type = typeof(FtpRemoteSession).Assembly.GetType("IoFtp.Core.Transport.OpenSslTlsStream")!;
            var task = (Task)type.GetMethod("AuthenticateAsync",BindingFlags.Public|BindingFlags.Static)!
                .Invoke(null,[client.Client,transport,"localhost",true,timeout.Token])!;
            await task.WaitAsync(timeout.Token);
            using var stream = (Stream)task.GetType().GetProperty("Result")!.GetValue(task)!;
            type.GetProperty("DiagnosticContext")!.SetValue(stream,
                (Func<string>)(() => "site=TEST; channel=data; last-command=RETR /release/file.rar"));
            var diagnostic = (Exception)type.GetMethod("CreateIoException", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(stream, ["read failed", 5])!;
            foreach (var detail in new[] { "site=TEST", "channel=data", "RETR /release/file.rar", "TLS=", "cipher=", "SSL_get_error=5" })
                if (!diagnostic.Message.Contains(detail)) throw new Exception("TLS diagnostic missing " + detail);
            await stream.WriteAsync(new byte[] {7}, timeout.Token);
            var bytes=new byte[4];
            await stream.ReadExactlyAsync(bytes, timeout.Token);
            if(!bytes.SequenceEqual(new byte[]{1,2,3,4})) throw new Exception("OpenSSL delayed reads lost data.");
            if(await stream.ReadAsync(new byte[1], timeout.Token)!=0) throw new Exception("TLS close_notify must return EOF.");
            await server;
            Console.WriteLine("PASS: OpenSSL nonblocking handshake, delayed fragmented reads, writes, TLS close_notify, error context and credential redaction.");
        }
        finally { listener.Stop(); }
    }
}
