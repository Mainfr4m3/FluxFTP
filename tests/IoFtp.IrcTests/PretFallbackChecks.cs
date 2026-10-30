using System.Reflection;
using System.Text;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;

internal static class PretFallbackChecks
{
    public static async Task Run()
    {
        var checks = 0;
        foreach (var advertised in new[] { false, true })
        foreach (var mode in new[] { "destination-cpsv", "destination-epsv-cpsv", "destination-epsv-success", "source-cpsv", "reprepare-denied", "no-pret" })
        {
            var reverse = mode == "source-cpsv";
            var epsv = mode.StartsWith("destination-epsv");
            var cpsvSuccess = mode == "destination-epsv-success";
            var denied = mode == "reprepare-denied";
            var needsPret = mode != "no-pret";
            var command = reverse ? "RETR file.rar" : "STOR file.rar";
            var passiveCommands = new List<string>();
            var passiveReplies = new List<string>();
            void Add(string verb, string reply) { passiveCommands.Add(verb); passiveReplies.Add(reply); }
            Add("CWD /release", "250 CWD");
            Add("TYPE I", "200 Binary");
            if (needsPret) Add("PRET " + command, "200 PRET");
            if (epsv)
            {
                Add("EPSV", "500 EPSV rejected; pre-transfer state cleared");
                Add("PRET " + command, "200 PRET");
            }
            Add("CPSV", cpsvSuccess ? "227 Entering Passive Mode (95,211,177,207,165,240)" : "500 CPSV rejected; pre-transfer state cleared");
            if (!cpsvSuccess)
            {
                if (needsPret) Add("PRET " + command, denied ? "550 Permission denied" : "200 PRET");
                if (!denied)
                {
                    Add("PASV", "227 Entering Passive Mode (95,211,177,207,165,240)");
                    Add("SSCN OFF", "200 SSCN:SERVER METHOD");
                }
            }
            if (!denied) Add(reverse ? "RETR file.rar" : "STOR file.rar", "150 Opening data connection");
            if (!denied) passiveReplies.Add("226 Transfer complete");
            var activeReplies = new List<string> { "250 CWD", "200 Binary" };
            if (!denied)
            {
                activeReplies.Add("200 SSCN role set");
                activeReplies.Add("200 PORT accepted");
                activeReplies.Add("150 Opening data connection");
                activeReplies.Add("226 Transfer complete");
            }
            using var passiveReader = new StreamReader(new MemoryStream(Encoding.ASCII.GetBytes(string.Join("\r\n", passiveReplies) + "\r\n")));
            using var activeReader = new StreamReader(new MemoryStream(Encoding.ASCII.GetBytes(string.Join("\r\n", activeReplies) + "\r\n")));
            using var passiveOutput = new MemoryStream();
            using var activeOutput = new MemoryStream();
            using var passiveWriter = new StreamWriter(passiveOutput, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
            using var activeWriter = new StreamWriter(activeOutput, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
            FtpRemoteSession Session(bool passive)
            {
                var session = new FtpRemoteSession();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(FtpRemoteSession).GetField("_profile", flags)!.SetValue(session,
                    new ConnectionProfile(Guid.NewGuid(), passive ? "JBG" : "OTHER", "example.invalid", 21, "test", TransferProtocol.FtpsExplicit,
                        Options: new SiteOptions(NeedsPret: passive && needsPret && !advertised, CeprSupported: passive && epsv)));
                typeof(FtpRemoteSession).GetField("_reader", flags)!.SetValue(session, passive ? passiveReader : activeReader);
                typeof(FtpRemoteSession).GetField("_writer", flags)!.SetValue(session, passive ? passiveWriter : activeWriter);
                typeof(FtpRemoteSession).GetProperty("IsConnected")!.SetValue(session, true);
                var features = new HashSet<string> { "SSCN" };
                if (passive) features.Add("CPSV");
                if (passive && needsPret && advertised) features.Add("PRET");
                typeof(FtpRemoteSession).GetProperty("Capabilities")!.SetValue(session, features);
                return session;
            }
            var passiveSession = Session(true);
            var activeSession = Session(false);
            var from = reverse ? passiveSession : activeSession;
            var to = reverse ? activeSession : passiveSession;
            try
            {
                await from.FxpToAsync(to, "/release/file.rar", "/release/file.rar", default, reverseDataConnection: reverse);
                if (denied) throw new Exception("Rejected re-PRET was ignored");
                if (from.LastFxpTransferElapsed <= TimeSpan.Zero) throw new Exception("Successful fallback lost timing");
            }
            catch (FtpCommandException ex) when (denied && ex.StatusCode == 550 && ex.Site == "JBG" && ex.Command == "PRET")
            {
                if (from.LastFxpTransferElapsed != TimeSpan.Zero) throw new Exception("Failed re-PRET counted as successful transfer");
            }
            var sentPassive = Encoding.UTF8.GetString(passiveOutput.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (!sentPassive.SequenceEqual(passiveCommands))
                throw new Exception($"PRET fallback sequence {mode}: expected {string.Join(" | ", passiveCommands)}, got {string.Join(" | ", sentPassive)}");
            var sentActive = Encoding.UTF8.GetString(activeOutput.ToArray());
            if (sentActive.Contains("PROT C") || sentPassive.Contains("PROT C")) throw new Exception("Secure fallback disabled encryption");
            checks++;
        }
        Console.WriteLine($"PASS: {checks} PRET fallback cases, configured/advertised support, destination EPSV/CPSV recovery, reverse source recovery, rejection and unchanged encryption.");
    }
}
