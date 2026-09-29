using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IoFtp.Core.Models;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;

internal static class IrcRoutingChecks
{
    public static async Task Run()
    {
        var count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("Routing: " + name); count++; }
        Check(IrcFish.Encrypt("Hello guys!", "ThisIsNOTsecure") == "+OK zzPVJ0.imLT1rN4oA.CbFjU1", "slftp ECB vector");
        Check(IrcFish.TryDecrypt("+OK *CXafm5eIlo8mQ12vArFSS6GLQYmTaGGF", "cbc:asdf1234", out var clear) && clear == "Hello guys!", "slftp CBC vector");
        foreach (var key in new[] { "test1234", "cbc:test1234" })
        {
            Check(IrcFish.TryDecrypt(IrcFish.Encrypt("Åäö release-GROUP", key), key, out clear) && clear == "Åäö release-GROUP", "Unicode roundtrip");
            Check(!IrcFish.TryDecrypt("+OK *invalid", key, out _), "malformed rejected");
        }
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-routing-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "routing.json");
            var store = new IrcRoutingStore(path);
            var catcher = new IrcCatcher(_ => ["TV"]);
            var site = new ConnectionProfile(Guid.NewGuid(), "Test", "localhost", 21, "test", TransferProtocol.FtpsExplicit);
            var commands = new IrcSetupCommands(() => [site], _ => { }, new SiteRuleStore(directory), site.Id, store, catcher);
            string Run(string command) => commands.Execute(command);
            foreach (var command in new[] { "!ircchanadd Default #announce", "!ircchanblow Default #announce cbc:test1234", "!ircchankey Default #announce joinsecret", "!catchadd Test Default #announce Bot PRE PRE,TV" })
                Check(Run(command).StartsWith("OK:"), command.Split(' ')[0]);
            Check(!File.ReadAllText(path).Contains("test1234") && !File.ReadAllText(path).Contains("joinsecret"), "keys protected");
            Check(store.Load().Channels[0].FishKey == "cbc:test1234", "key load");
            Check(Run("!catchtest Default #announce Bot PRE TV Show.S01E01-GROUP").Contains("Show.S01E01-GROUP"), "dry run match");
            Check(catcher.Recent().Count == 0, "dry run no mutation");
            Check(Run("!catchtest Default #announce Bot (PRE) [TV] Show.S01E01-GROUP").Contains("Show.S01E01-GROUP"), "decorated tokens");
            foreach (var command in new[] { "!catchtest Default #announce Fake PRE TV Show.S01E01-GROUP", "!catchtest Default #else Bot PRE TV Show.S01E01-GROUP", "!catchtest Default #announce Bot PREVIEW TV Show.S01E01-GROUP", "!catchtest Default #announce Bot PRE Show.S01E01-GROUP" })
                Check(Run(command) == "OK: No match.", "strict match");
            Check(Run("!ircchanadd Missing #x").StartsWith("ERROR:"), "missing network");
            Check(Run("!ircchanblow Default #announce bad").StartsWith("ERROR:"), "short key rejected");
            Check(Run("!ircnetadd Other 127.0.0.1:6667 plain").StartsWith("OK:"), "network add");
            Check(Run("!ircnetpass Other secretpass").StartsWith("OK:") && !File.ReadAllText(path).Contains("secretpass"), "network password protected");
            Check(Run("!ircnetdel Other").StartsWith("OK:") && store.Load().Networks.Length == 0, "network delete");

            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var options = new IrcSettings(Enabled: true, Host: "127.0.0.1", Port: ((IPEndPoint)listener.LocalEndpoint).Port, UseTls: false);
            var controlCalls = 0;
            await using (var service = new IrcService(new GlobalSettings(Irc: options), _ => { }, setupCommand: (_, _) => { controlCalls++; return Task.FromResult("BAD"); }, routingStore: store, catcher: catcher, controlEnabled: false))
            {
                using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(client.GetStream());
                using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
                async Task<string> Read() => await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8)) ?? throw new Exception("closed");
                async Task Until(string expected) { for (var i = 0; i < 12; i++) if ((await Read()).Contains(expected)) return; throw new Exception("Missing " + expected); }
                await Until("USER fluxftp");
                await writer.WriteLineAsync(":server 001 FluxFTP :Welcome");
                await Until("JOIN #announce joinsecret"); Check(true, "joined extra channel with key");
                var cipher = IrcFish.Encrypt("PRE TV Show.S01E01-GROUP", "cbc:test1234");
                await writer.WriteLineAsync("@batch=old :Bot!u@h PRIVMSG #announce :" + cipher);
                await writer.WriteLineAsync(":Bot!u@h PRIVMSG #announce :PRE TV Plain.Release-GROUP");
                await writer.WriteLineAsync(":Fake!u@h PRIVMSG #announce :" + cipher);
                await writer.WriteLineAsync("PING :barrier"); await Until("PONG :barrier");
                Check(catcher.Recent().Count == 0, "history/plaintext/wrong bot rejected");
                await writer.WriteLineAsync(":Bot!u@h PRIVMSG #announce :" + cipher);
                await writer.WriteLineAsync(":Bot!u@h NOTICE #announce :" + cipher);
                await writer.WriteLineAsync("PING :caught"); await Until("PONG :caught");
                Check(catcher.Recent().Count == 1 && catcher.Recent()[0].Section == "TV", "encrypted announcement deduped");
                await writer.WriteLineAsync("@account=admin :user!u@h PRIVMSG FluxFTP :!slots Test 4");
                await writer.WriteLineAsync("PING :control"); await Until("PONG :control");
                Check(controlCalls == 0, "secondary cannot control");
                Check(Run("!ircchanadd Default #second").StartsWith("OK:"), "dynamic channel saved");
                await Until("JOIN #second"); Check(true, "dynamic channel joined");
                Check(Run("!ircchandel Default #second").StartsWith("OK:"), "dynamic channel removed");
                await Until("PART #second"); Check(true, "dynamic channel parted");
            }
            Check(Run("!catchdel 1").StartsWith("OK:") && store.Load().Catches.Length == 0, "catch deletion");
            using var primaryListener = new TcpListener(IPAddress.Loopback, 0); primaryListener.Start();
            using var secondaryListener = new TcpListener(IPAddress.Loopback, 0); secondaryListener.Start();
            Check(Run($"!ircnetadd Extra 127.0.0.1:{((IPEndPoint)secondaryListener.LocalEndpoint).Port} plain ExtraBot").StartsWith("OK:"), "manager network setup");
            var primary = options with { Port = ((IPEndPoint)primaryListener.LocalEndpoint).Port, Channel = "#control" };
            await using (var manager = new IrcManager(new GlobalSettings(Irc: primary), _ => { }, (_, _) => Task.FromResult("OK:"), store, catcher))
            {
                using var mainClient = await primaryListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                using var extraClient = await secondaryListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                using var extraReader = new StreamReader(extraClient.GetStream());
                var registration = new List<string>();
                while (registration.Count < 3) registration.Add(await extraReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) ?? "");
                Check(registration.Contains("NICK ExtraBot"), "manager starts extra network");
                Check(Run("!ircnetdel Extra").StartsWith("OK:"), "manager remove saved");
                Check(await extraReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8)) is null, "manager stops removed connection");
            }
            Console.WriteLine($"PASS: {count} IRC routing/FiSH checks, including simulated-server integration.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
