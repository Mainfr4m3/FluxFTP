using System.Reflection;
using System.Text;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;

internal static class SecureReverseFxpChecks
{
    public static async Task Run()
    {
        foreach (var sscnFallback in new[] { false, true })
        foreach (var rejected in new[] { false, true })
        {
            var sourceReplies = "250 CWD\r\n200 Binary\r\n200 PRET\r\n" +
                (sscnFallback ? "227 Entering Passive Mode (95,211,177,207,165,240)\r\n200 SSCN:SERVER METHOD\r\n" :
                    "227-Using RACED1 for upcoming transfer\r\n227 Entering Passive Mode (95,211,177,207,165,240)\r\n") +
                "150 File status okay\r\n226 Transfer complete\r\n";
            var targetReplies = "250 CWD\r\n200 Binary\r\n" +
                (sscnFallback ? "200 SSCN:CLIENT METHOD\r\n" : "220 SSCN:SERVER METHOD\r\n") +
                "200 PORT command successful\r\n" + (rejected ? "553 File exists\r\n" : "150 Opening BINARY data connection using SSL/TLS\r\n226 Transfer complete\r\n");
            using var sourceInput = new MemoryStream(Encoding.ASCII.GetBytes(sourceReplies));
            using var targetInput = new MemoryStream(Encoding.ASCII.GetBytes(targetReplies));
            using var sourceReader = new StreamReader(sourceInput);
            using var targetReader = new StreamReader(targetInput);
            using var sourceOutput = new MemoryStream();
            using var targetOutput = new MemoryStream();
            using var sourceWriter = new StreamWriter(sourceOutput, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
            using var targetWriter = new StreamWriter(targetOutput, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
            FtpRemoteSession Session(string site, StreamReader reader, StreamWriter writer, bool source)
            {
                var session = new FtpRemoteSession();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(FtpRemoteSession).GetField("_profile", flags)!.SetValue(session,
                    new ConnectionProfile(Guid.NewGuid(), site, "master.example", 21, "test", TransferProtocol.FtpsExplicit,
                        Options: new SiteOptions(NeedsPret: source)));
                typeof(FtpRemoteSession).GetField("_reader", flags)!.SetValue(session, reader);
                typeof(FtpRemoteSession).GetField("_writer", flags)!.SetValue(session, writer);
                typeof(FtpRemoteSession).GetProperty("IsConnected")!.SetValue(session, true);
                typeof(FtpRemoteSession).GetProperty("Capabilities")!.SetValue(session,
                    new HashSet<string>(sscnFallback ? ["SSCN"] : source ? ["CPSV"] : ["SSCN"]));
                return session;
            }
            var from = Session("drFTPD", sourceReader, sourceWriter, true);
            var to = Session("ioFTPD", targetReader, targetWriter, false);
            var failed = false;
            try { await from.FxpToAsync(to, "/release/file.rar", "/release/file.rar", default, reverseDataConnection: true); }
            catch (FtpCommandException exception) when (exception.StatusCode == 553) { failed = true; }
            if ((!failed && from.LastFxpTransferElapsed <= TimeSpan.Zero) || (failed && from.LastFxpTransferElapsed != TimeSpan.Zero)) throw new Exception("Passive timing must require successful replies from both servers.");
            if (failed != rejected) throw new Exception("Secure reverse FXP misclassified rejection.");
            var sentSource = Encoding.UTF8.GetString(sourceOutput.ToArray()).TrimStart('\uFEFF');
            var sentTarget = Encoding.UTF8.GetString(targetOutput.ToArray()).TrimStart('\uFEFF');
            if (!sentSource.Contains("PRET RETR file.rar\r\n") || !sentSource.Contains(sscnFallback ? "PASV\r\nSSCN OFF\r\n" : "CPSV\r\n") ||
                !sentTarget.Contains("PORT 95,211,177,207,165,240\r\nSTOR file.rar\r\n") ||
                sentSource.Contains("PROT C") || sentTarget.Contains("PROT C") || sentTarget.Contains("CPSV"))
                throw new Exception("Secure reverse FXP did not preserve the source slave endpoint and TLS roles.");
            if (!from.LastFxpNegotiation.Contains("source")) throw new Exception("Missing reverse FXP route diagnostic.");
        }
        Console.WriteLine("PASS: source CPSV and source PASV/SSCN secure FXP, PRET, slave IP, PORT/STOR direction and 553 rejection.");
    }
}
