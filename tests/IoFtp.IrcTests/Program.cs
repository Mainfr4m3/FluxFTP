using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using IoFtp.Core.Abstractions;
using IoFtp.Core.Models;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;

var assertions = 0;
if (args.FirstOrDefault() == "--race-flow-checks")
{
    await RaceFlowChecks.Run();
    return;
}
if (args.FirstOrDefault() == "--rush-migration-checks")
{
    FtpRushMigrationChecks.Run();
    foreach (var path in args.Skip(1))
    {
        var package = FtpRushSiteImporter.ImportPackage(path);
        var rows = FtpRushSectionMigration.Preview(package.Bookmarks);
        Console.WriteLine($"Read {Path.GetFileName(path)}: {package.Sites.Count} sites, {package.Bookmarks.Count} bookmarks, {rows.Count} section candidates; no files written.");
    }
    return;
}
if (args.FirstOrDefault() == "--visionary-checks")
{
    VisionaryConfigurationChecks.Run();
    VisionaryRaceChecks.Run();
    VisionaryPayloadChecks.Run();
    foreach (var path in args.Skip(1))
    {
        var profiles = VisionaryTransferProfiles.Load(path);
        Console.WriteLine($"Validated sample/covers profiles from {Path.GetFileName(path)}; original unchanged.");
    }
    return;
}
if (args.FirstOrDefault() == "--visionary-files")
{
    foreach (var path in args.Skip(1))
    {
        var file = new VisionaryConfiguration(path);
        if (file.HasChanges) throw new Exception("Imported file incorrectly marked changed.");
        Console.WriteLine($"Parsed {Path.GetFileName(path)}: {file.Entries.Count} entries; original unchanged.");
    }
    return;
}
if (!IrcConnectionError.RegistrationReply("464", "secret")!.Contains("password rejected") ||
    !IrcConnectionError.RegistrationReply("433", "secret")!.Contains("Nickname") ||
    IrcConnectionError.RegistrationReply("ERROR", "password secret")!.Contains("secret") ||
    IrcConnectionError.Describe(new System.IO.IOException("secret")).Contains("secret")) throw new Exception("IRC connection diagnostics failed");
var projectLines = IrcProjectNews.Parse("""
{"draft":false,"tag_name":"v1.0.53","published_at":"2026-09-29T12:00:00Z","prerelease":false}
""");
if (projectLines.Length != 1 || !projectLines[0].Contains("2026-09-29 12:00 UTC") || !projectLines[0].Contains("release v1.0.53") || !projectLines[0].EndsWith("Mainfr4m3/FluxFTP/releases/tag/v1.0.53")) throw new Exception("Project news parse failed");
ProfileStoreChecks.Run();
GlobalSettingsChecks.Run();
VisionaryConfigurationChecks.Run();
VisionaryRaceChecks.Run();
VisionaryPayloadChecks.Run();
FtpRushMigrationChecks.Run();
var ircApiRequest = new IrcConnectRequest("leon.example.test", 29025, "FLUXFTP", "#FLUX",
    UseZnc: true, ZncUsername: "FLUX", ZncNetwork: "FreakNET", Password: "password:with:colons");
var ircApiSettings = ircApiRequest.Apply(new IrcSettings(Host: "other.example.test", AccountLinks: [new("old", "admin")]));
if (!ircApiSettings.Enabled || ircApiSettings.Password != "password:with:colons" || ircApiSettings.AccountLinks!.Length != 0 || ircApiSettings.ZncNetwork != "FreakNET") throw new Exception("IRC API settings conversion failed");
foreach (var invalid in new[] { ircApiRequest with { Port = 0 }, ircApiRequest with { Host = "host\r\nPASS bad" }, ircApiRequest with { Channel = "#a,#b" }, ircApiRequest with { Password = "bad\npassword" }, ircApiRequest with { ZncUsername = "" } })
{
    var rejected = false;
    try { invalid.Apply(null); } catch (ArgumentException) { rejected = true; }
    if (!rejected) throw new Exception("Invalid IRC API request accepted");
}
RaceLogChecks.Run();
await IrcRoutingChecks.Run();
await IrcConnectionChecks.Run();
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    assertions++;
}

Check(IrcProtocol.IsAdmin(new(200, "200-Flags: 1A\r\n200 End")), "SiteOp");
Check(IrcProtocol.IsAdmin(new(200, "200-\u001b[31mFlags: M\u001b[0m")), "Master with ANSI color");
Check(!IrcProtocol.IsAdmin(new(200, "Flags: V3\nUser: Master1")), "non-admin");
Check(!IrcProtocol.IsAdmin(new(550, "Flags: 1")), "FTP error is not authority");
Check(!IrcProtocol.IsAdmin(new(200, "Tagline: Flags: M\nFlags: 3")), "tagline is not authority");
Check(!IrcProtocol.IsAdmin(new(200, "Flags: 1\nFlags: 3")), "ambiguous flags rejected");
Check(!IrcProtocol.FtpUser("user\r\nSITE SHUTDOWN") && !IrcProtocol.FtpUser("*"), "FTP injection and wildcard rejected");
var parsed = IrcMessage.Parse("@account=admin;time=2026-09-28T12:00:00Z :nick!u@h PRIVMSG FluxFTP :!login bob password with spaces");
Check(parsed.Nick == "nick" && parsed.Parameters[1] == "!login bob password with spaces" && parsed.Tags["account"] == "admin", "IRC parser");
var now = DateTimeOffset.UtcNow;
Check(!IrcProtocol.IsLive(parsed, now, true), "old history ignored");
Check(!IrcProtocol.IsLive(IrcMessage.Parse(":n!u@h PRIVMSG #test :!op"), now, true), "ZNC requires timestamps");
Check(!IrcProtocol.IsLive(IrcMessage.Parse($"@batch=old;time={now:O} :n!u@h PRIVMSG #test :!op"), now, false), "batches ignored");
Check(IrcProtocol.Same("Test[User]", "test{user}"), "IRC casemapping");

var protect = typeof(GlobalSettingsStore).GetMethod("Protect", BindingFlags.NonPublic | BindingFlags.Static)!;
var unprotect = typeof(GlobalSettingsStore).GetMethod("UnprotectValue", BindingFlags.NonPublic | BindingFlags.Static)!;
var encrypted = (string)protect.Invoke(null, ["znc-secret"])!;
Check(encrypted.StartsWith("dpapi:") && !encrypted.Contains("znc-secret") && (string)unprotect.Invoke(null, [encrypted])! == "znc-secret", "password protection roundtrip");
Check(System.Text.Json.JsonSerializer.Deserialize<GlobalSettings>("{}")!.Irc is null, "old settings compatible");

var site = new ConnectionProfile(Guid.NewGuid(), "Test", "unused", 21, "lookup", TransferProtocol.FtpsExplicit, "lookup-secret");
var backend = new FakeBackend();
Check(await IrcFtpAccess.VerifyAsync(site, "admin", "correct password", backend.Create, default), "private login checks password and flags");
Check(backend.Logins.SequenceEqual(new[] { "admin", "lookup" }), "login identity then independent authority lookup");
backend.Logins.Clear();
try { await IrcFtpAccess.VerifyAsync(site, "admin", "wrong", backend.Create, default); Check(false, "wrong password throws"); }
catch (UnauthorizedAccessException) { Check(backend.Logins.Count == 1, "wrong password never reaches flag lookup"); }
Check(!await IrcFtpAccess.VerifyAsync(site, "regular", "correct password", backend.Create, default), "valid password without admin denied");
Check(!await IrcFtpAccess.VerifyAsync(site with { Protocol = TransferProtocol.Ftp }, "admin", "correct password", backend.Create, default), "plaintext FTP denied");
Check(!await IrcFtpAccess.VerifyAsync(site with { AllowInvalidCertificate = true }, "admin", null, backend.Create, default), "unverified FTPS denied");
Check(await IrcFtpAccess.VerifyAsync(site, "admin", null, backend.Create, default), "linked account checked against current flags");

using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var options = new IrcSettings(Enabled: true, Host: "127.0.0.1", Port: ((IPEndPoint)listener.LocalEndpoint).Port,
    UseTls: false, Channel: "#test", AdminSiteId: site.Id, AllowPrivateLogin: false,
    AccountLinks: [new("admin-account", "admin"), new("regular-account", "regular")]);
var logs = new List<string>();
await using (var bot = new IrcService(new GlobalSettings(Irc: options), s => { lock (logs) logs.Add(s); }, () => site, backend.Create,
    projectNews: _ => Task.FromResult<IReadOnlyList<string>>(["FluxFTP test build: https://github.com/Mainfr4m3/FluxFTP/releases"])))
{
    using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
    using var reader = new StreamReader(client.GetStream());
    using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
    async Task<string> Read() => (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5))) ?? throw new Exception("Disconnected");
    async Task Until(string expected)
    {
        for (var i = 0; i < 12; i++) if ((await Read()).Contains(expected, StringComparison.Ordinal)) return;
        throw new Exception("Missing reply: " + expected);
    }
    Check(await Read() == "CAP LS 302", "capability negotiation starts");
    await Until("USER fluxftp");
    await writer.WriteLineAsync(":server CAP FluxFTP LS :account-tag account-notify extended-join server-time batch");
    await Until("CAP REQ");
    await writer.WriteLineAsync(":server CAP FluxFTP ACK :account-tag account-notify extended-join server-time batch");
    await Until("CAP END");
    await writer.WriteLineAsync(":server 001 FluxFTP :Welcome");
    await Until("JOIN #test");

    await writer.WriteLineAsync(":admin-account!u@h PRIVMSG #test :!op");
    await Until("No verified account");
    Check(true, "nickname alone denied");
    await Task.Delay(1100);
    await writer.WriteLineAsync("@account=admin-account :legit!u@h PRIVMSG #test :!op");
    await Until("MODE #test +o legit");
    await Until("FTP admin verified");
    Check(true, "verified mapped account granted");
    await Task.Delay(1100);
    await writer.WriteLineAsync("@account=regular-account :regular!u@h PRIVMSG #test :!op");
    await Until("MODE #test -o regular");
    await Until("FTP admin verification failed");
    Check(true, "mapped non-admin denied");
    await Task.Delay(1100);
    await writer.WriteLineAsync(":public!u@h PRIVMSG #test :!login admin secret");
    await Until("private message");
    Check(true, "public credential command rejected");
    await Task.Delay(1100);
    var loginsBeforeNews = backend.Logins.Count;
    await writer.WriteLineAsync(":news!u@h PRIVMSG #test :!news");
    await Until("PRIVMSG #test :FluxFTP test build: https://github.com/Mainfr4m3/FluxFTP/releases");
    Check(backend.Logins.Count == loginsBeforeNews, "project news replies in channel without FTP login");
    await Task.Delay(1100);

    backend.WaitForLookup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    backend.LookupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await writer.WriteLineAsync("@account=admin-account :changing!u@h PRIVMSG #test :!op");
    await backend.LookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await writer.WriteLineAsync(":changing!u@h NICK :changed");
    await writer.WriteLineAsync("PING :during-ftp");
    Check(await Read() == "PONG :during-ftp", "PING handled during slow FTP check");
    backend.WaitForLookup.SetResult();
    await Task.Delay(200);
    await writer.WriteLineAsync("PING :after-nick");
    Check(await Read() == "PONG :after-nick", "no grant after identity change");

    await writer.WriteLineAsync("@batch=history;account=admin-account :replay!u@h PRIVMSG #test :!op");
    await writer.WriteLineAsync("PING :after-replay");
    Check(await Read() == "PONG :after-replay", "history cannot grant operator");
    await writer.WriteLineAsync("@account=admin-account :setup!u@h PRIVMSG FluxFTP :!slots Test 2");
    await writer.WriteLineAsync("@account=admin-account :setup!u@h PRIVMSG FluxFTP :!maxupdn Test 2 1");
    await writer.WriteLineAsync("@account=admin-account :setup!u@h PRIVMSG FluxFTP :!setdir Test MP3 /MP3");
    for (var i = 0; i < 3; i++)
        Check((await Read()).Contains("Setup commands require a private message and a TLS"), "queued setup lines processed in order, denied without TLS");
}
Check(logs.All(log => !log.Contains("secret") && !log.Contains("correct password")), "logs contain no passwords");
await using (var bot = new IrcService(new GlobalSettings(Irc: options with { UseZnc = true, ZncUsername = "zncuser", ZncNetwork = "network", Password = "znc-secret" }), _ => { }, () => site, backend.Create))
{
    using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
    using var reader = new StreamReader(client.GetStream());
    using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
    async Task<string> Read() => (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5))) ?? throw new Exception("Disconnected");
    Check(await Read() == "PASS :zncuser/network:znc-secret", "ZNC PASS format");
    Check(await Read() == "CAP LS 302", "ZNC capability negotiation after password");
    await Read(); await Read();
    await writer.WriteLineAsync(":server CAP FluxFTP LS :account-tag extended-join server-time batch");
    await Read();
    await writer.WriteLineAsync(":server CAP FluxFTP ACK :account-tag extended-join server-time batch");
    await Read();
    await writer.WriteLineAsync(":server 001 FluxFTP :Welcome");
    await Read();
    await writer.WriteLineAsync("@account=admin-account :missingtime!u@h PRIVMSG #test :!op");
    await writer.WriteLineAsync("PING :no-time");
    Check(await Read() == "PONG :no-time", "ZNC ignores messages without timestamp");
    await writer.WriteLineAsync($"@time={DateTimeOffset.UtcNow.AddHours(-1):O};account=admin-account :old!u@h PRIVMSG #test :!op");
    await writer.WriteLineAsync("PING :old-time");
    Check(await Read() == "PONG :old-time", "ZNC ignores old playback");
    await writer.WriteLineAsync($"@time={DateTimeOffset.UtcNow:O};batch=history;account=admin-account :batch!u@h PRIVMSG #test :!op");
    await writer.WriteLineAsync("PING :batched");
    Check(await Read() == "PONG :batched", "ZNC ignores recent batched playback");
    await writer.WriteLineAsync($"@time={DateTimeOffset.UtcNow:O};account=admin-account :live!u@h PRIVMSG #test :!op");
    Check(await Read() == "MODE #test +o live", "ZNC accepts live verified command");
    await Read();
    await Task.Delay(1100);
    await writer.WriteLineAsync($"@time={DateTimeOffset.UtcNow:O} :joined!u@h JOIN #test admin-account :Real Name");
    Check(await Read() == "MODE #test +o joined", "extended JOIN automatically verifies linked admin");
}
Console.WriteLine($"PASS: {assertions} IRC checks, including simulated-server integration.");
SiteRuleChecks.Run();
IrcSetupChecks.Run();

sealed class FakeBackend
{
    public List<string> Logins { get; } = [];
    public TaskCompletionSource? WaitForLookup { get; set; }
    public TaskCompletionSource? LookupStarted { get; set; }
    public IRemoteSession Create() => new Session(this);
    private sealed class Session(FakeBackend owner) : IRemoteSession
    {
        public bool IsConnected => true;
        public IReadOnlySet<string> Capabilities => new HashSet<string>();
        public Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            owner.Logins.Add(profile.Username);
            if (profile.Username != "lookup" && profile.Password != "correct password") throw new UnauthorizedAccessException();
            return Task.CompletedTask;
        }
        public async Task<RemoteCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
        {
            owner.LookupStarted?.TrySetResult();
            if (owner.WaitForLookup is { } pending) await pending.Task.WaitAsync(cancellationToken);
            return new(200, command == "SITE USER admin" ? "200-Flags: 1M" : "200-Flags: 3");
        }
        public Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RemoteEntry>>([new("Newest.Release", "/Newest.Release", true, null, DateTimeOffset.UtcNow)]);
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task DownloadAsync(string remotePath, Stream destination, long offset, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UploadAsync(string remotePath, Stream source, long offset, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
