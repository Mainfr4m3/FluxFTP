using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Text;
using System.Text.Json;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;

internal static class SiteCompatibilityChecks
{
    public static async Task Run()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var rsa = RSA.Create(2048);
        using var generated = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx));
        foreach (var native in new[] { false, true })
        foreach (var early in new[] { false, true })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var commands = new List<string>();
            var server = Task.Run(async () =>
            {
                using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                Stream stream = peer.GetStream();
                async Task<string> Read()
                {
                    var bytes = new List<byte>(); var b = new byte[1];
                    while (await stream.ReadAsync(b, timeout.Token) > 0) { if (b[0] == 10) break; if (b[0] != 13) bytes.Add(b[0]); }
                    var command = Encoding.UTF8.GetString(bytes.ToArray()); commands.Add(command); return command;
                }
                Task Reply(string line) => stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\r\n"), timeout.Token).AsTask();
                await Reply("220 Ready");
                if (await Read() != "AUTH SSL") throw new Exception("Forced AUTH SSL lost");
                await Reply("234 TLS ready");
                using var tls = new SslStream(stream);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
                stream = tls;
                if (tls.SslProtocol != SslProtocols.Tls12) throw new Exception("TLS 1.2 restriction ignored");
                if (early) { if (await Read() != "PBSZ 0") throw new Exception("Early PBSZ"); await Reply("200 OK"); }
                if (await Read() != "USER räksmörgås") throw new Exception("UTF-8 login bytes"); await Reply("331 Password");
                if (await Read() != "PASS sömlös") throw new Exception("UTF-8 password bytes"); await Reply("230 Logged in");
                if (!early) { if (await Read() != "PBSZ 0") throw new Exception("Post-login PBSZ"); await Reply("200 OK"); }
                if (await Read() != "PROT P") throw new Exception("Protection"); await Reply("200 OK");
                if (await Read() != "TYPE I") throw new Exception("Binary"); await Reply("200 OK");
                if (await Read() != "FEAT") throw new Exception("FEAT"); await Reply("211-Features\r\n211 End");
                if (await Read() != "OPTS UTF8 ON") throw new Exception("Forced UTF-8 without FEAT"); await Reply("500 Not supported");
                if (await Read() != "CWD /åäö") throw new Exception("UTF-8 path bytes"); await Reply("250 OK");
                if (await Read() != "QUIT") throw new Exception("QUIT"); await Reply("221 Bye");
            });
            var options = new SiteOptions(UseOpenSslTls: native, TextEncoding: FtpTextEncoding.Utf8, Utf8Login: true,
                AuthCommand: FtpAuthCommand.Ssl, PbszBeforeLogin: early, Tls12Only: true);
            if (JsonSerializer.Deserialize<SiteOptions>(JsonSerializer.Serialize(options)) != options) throw new Exception("Options persistence");
            await using var session = new FtpRemoteSession();
            var profile = new ConnectionProfile(Guid.NewGuid(), "test", "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
                "räksmörgås", TransferProtocol.FtpsExplicit, "sömlös", true, Options: options);
            try
            {
                await session.ConnectAsync(profile, timeout.Token);
                await session.ExecuteCommandAsync("CWD /åäö", timeout.Token);
                await session.DisconnectAsync(timeout.Token);
                await server;
                if (commands.Count(c => c == "PBSZ 0") != 1) throw new Exception("Duplicate PBSZ");
            }
            finally { listener.Stop(); }
        }
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var encoding in new[] { Encoding.UTF8, Encoding.GetEncoding(1252) })
        {
            await using var session = new FtpRemoteSession();
            typeof(FtpRemoteSession).GetField("_controlEncoding", flags)!.SetValue(session, encoding);
            using var data = new MemoryStream(encoding.GetBytes("åäö.txt\r\n"));
            var text = await (Task<string>)typeof(FtpRemoteSession).GetMethod("ReadListingDataAsync", flags)!.Invoke(session, [data, CancellationToken.None])!;
            if (text != "åäö.txt\r\n") throw new Exception("Listing charset ignored");
        }
        if (JsonSerializer.Deserialize<SiteOptions>("{}")!.TextEncoding != FtpTextEncoding.Auto) throw new Exception("Migration default");
        Console.WriteLine("PASS: 4 TLS/UTF-8 connection combinations, AUTH SSL, PBSZ ordering, TLS 1.2 on both backends, UTF-8 login/path bytes, listing encodings and settings migration.");
    }
}

