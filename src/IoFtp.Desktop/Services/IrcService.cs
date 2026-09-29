using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using IoFtp.Core.Models;
using IoFtp.Core.Abstractions;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

/// <summary>IRC connection with optional admin control and announcement channels. Traffic and credentials are never logged.</summary>
internal sealed class IrcService : IAsyncDisposable
{
    private readonly GlobalSettings _global;
    private readonly IrcSettings _settings;
    private readonly Action<string> _log;
    private readonly Func<ConnectionProfile?> _loadProfile;
    private readonly Func<IRemoteSession> _createSession;
    private readonly Func<string, Func<bool>, Task<string>> _setupCommand;
    private readonly IrcRoutingStore _routingStore;
    private readonly IrcCatcher _catcher;
    private readonly bool _controlEnabled;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _projectNews;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;

    public IrcService(GlobalSettings settings, Action<string> log, Func<ConnectionProfile?>? loadProfile = null,
        Func<IRemoteSession>? createSession = null, Func<string, Func<bool>, Task<string>>? setupCommand = null,
        IrcRoutingStore? routingStore = null, IrcCatcher? catcher = null, bool controlEnabled = true,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? projectNews = null)
    {
        _global = settings; _settings = settings.Irc ?? new(); _log = log;
        _loadProfile = loadProfile ?? (() => new ProfileStore().Load().SingleOrDefault(p => p.Id == _settings.AdminSiteId));
        _createSession = createSession ?? (() => new FtpRemoteSession());
        _setupCommand = setupCommand ?? ((_, _) => Task.FromResult("ERROR: Setup commands are unavailable in this host."));
        _routingStore = routingStore ?? new(); _catcher = catcher ?? new(); _controlEnabled = controlEnabled;
        _projectNews = projectNews ?? new IrcProjectNews().GetAsync;
        _run = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await ConnectAsync(_stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) { _log($"IRC [{_settings.NetworkName}]: {IrcConnectionError.Describe(ex)} Retrying in 15 seconds."); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ConnectAsync(CancellationToken stopping)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var token = connection.Token;
        using var tcp = new TcpClient();
        _log($"IRC [{_settings.NetworkName}] connecting to {_settings.Host}:{_settings.Port} ({(_settings.UseTls ? "TLS" : "plain")}, {(_settings.UseZnc ? "ZNC" : "direct")})...");
        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            await tcp.ConnectAsync(_settings.Host, _settings.Port, connectTimeout.Token);
        }
        using Stream stream = _settings.UseTls
            ? new SslStream(tcp.GetStream(), false, (_, _, _, errors) => AcceptCertificate(_settings, errors))
            : tcp.GetStream();
        if (stream is SslStream tls)
        {
            using var tlsTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            tlsTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = _settings.Host }, tlsTimeout.Token);
        }
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { NewLine = "\r\n", AutoFlush = true };
        using var writes = new SemaphoreSlim(1);
        var started = DateTimeOffset.UtcNow;
        var epoch = 0L;
        var nick = _settings.Nick;
        var authenticated = new ConcurrentDictionary<string, (string User, DateTimeOffset Until)>(StringComparer.Ordinal);
        var identities = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        var requests = Channel.CreateBounded<Request>(new BoundedChannelOptions(16) { SingleReader = true, SingleWriter = true });
        var cooldowns = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var identitySeen = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var nextBusyNotice = DateTimeOffset.MinValue;
        var nextRequest = DateTimeOffset.MinValue;
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var acknowledged = new HashSet<string>(StringComparer.Ordinal);
        var registered = false;
        var routing = _routingStore.Load();
        var requestedChannels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pendingJoins = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var lastReceived = DateTimeOffset.UtcNow;
        var lastPing = lastReceived;
        var nextRefresh = lastReceived;

        async Task Send(string line)
        {
            if (line.Any(c => c is '\r' or '\n' or '\0') || Encoding.UTF8.GetByteCount(line) > 510)
                throw new InvalidDataException("Invalid outgoing IRC line.");
            await writes.WaitAsync(token);
            try { await writer.WriteLineAsync(line.AsMemory(), token); }
            finally { writes.Release(); }
        }
        Task Notice(string target, string message) => Send($"NOTICE {target} :{Clean(message)}");
        async Task RefreshChannels()
        {
            routing = _routingStore.Load();
            if (!registered) return;
            var channels = routing.Channels.Where(c => c.Network.Equals(_settings.NetworkName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (_controlEnabled && !channels.Any(c => IrcProtocol.Same(c.Name, _settings.Channel))) channels.Add(new(_settings.NetworkName, _settings.Channel));
            foreach (var old in requestedChannels.Keys.Where(old => !channels.Any(c => IrcProtocol.Same(old, c.Name))).ToArray())
            { await Send($"PART {old}"); requestedChannels.Remove(old); pendingJoins.Remove(old); }
            foreach (var channel in channels)
            {
                if (!requestedChannels.TryGetValue(channel.Name, out var key) || key != channel.JoinKey)
                {
                    await Send($"JOIN {channel.Name}{(channel.JoinKey.Length == 0 ? "" : " " + channel.JoinKey)}");
                    requestedChannels[channel.Name] = channel.JoinKey;
                    pendingJoins[channel.Name] = DateTimeOffset.UtcNow;
                }
            }
            foreach (var pending in pendingJoins.Where(p => DateTimeOffset.UtcNow - p.Value > TimeSpan.FromSeconds(30)).ToArray())
            {
                _log($"IRC [{_settings.NetworkName}]: no JOIN confirmation for {pending.Key}. Check ZNC's upstream connection, network selection and channel access.");
                pendingJoins.Remove(pending.Key);
            }
        }
        bool Current(Request request) => identities.TryGetValue(request.Prefix, out var version) && request.Epoch == version && !token.IsCancellationRequested;
        void Invalidate(string prefix)
        {
            identities.TryRemove(prefix, out _);
            authenticated.TryRemove(prefix, out _);
        }

        var worker = Task.Run(async () =>
        {
            await foreach (var request in requests.Reader.ReadAllAsync(token))
            {
                try
                {
                    if (!Current(request)) continue;
                    var parts = request.Text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    var command = parts[0].ToLowerInvariant();
                    if (command == "!news")
                    {
                        foreach (var item in await _projectNews(token))
                        {
                            if (!Current(request)) break;
                            var target = request.Private ? request.Nick : _settings.Channel;
                            var message = Clean(item);
                            var key = routing.Channels.FirstOrDefault(c => c.Network.Equals(_settings.NetworkName, StringComparison.OrdinalIgnoreCase) && IrcProtocol.Same(c.Name, target))?.FishKey;
                            if (!request.Private && !string.IsNullOrEmpty(key)) message = IrcFish.Encrypt(message, key);
                            await Send($"PRIVMSG {target} :{message}");
                            await Task.Delay(1000, token);
                        }
                        continue;
                    }
                    if (command == "!help")
                    {
                        if (parts.Length > 1 && parts[1].Equals("setup", StringComparison.OrdinalIgnoreCase))
                        {
                            await Notice(request.Nick, "Private setup: !addsite SITE USER PASSWORD HOST:PORT [explicit|implicit|off]; !slots SITE N; !maxupdn SITE UP DOWN; !maxidle SITE SECONDS; !tls SITE explicit|implicit|off.");
                            await Task.Delay(500, token);
                            await Notice(request.Nick, "!setaffils SITE GROUPS; !setdir SITE SECTION /path; !ruleadd SITE SECTION|* if CONDITION then allow|drop; !site SITE; !sites; !siterules SITE. Quote names/passwords with spaces. Wait for OK/ERROR for each line.");
                            await Task.Delay(500, token);
                            await Notice(request.Nick, "Networks: !ircnetlist; !ircnetadd NETWORK HOST:PORT tls|plain [NICK]; !ircnetpass NETWORK [PASSWORD]; !ircnetdel NETWORK. Primary: " + _settings.NetworkName);
                            await Task.Delay(500, token);
                            await Notice(request.Nick, "Channels: !ircchanadd/!ircchandel NETWORK #CHANNEL; !ircchanlist NETWORK; !ircchanblow/!ircchankey NETWORK #CHANNEL [KEY]. FiSH CBC keys start cbc:; otherwise ECB. Omit KEY to remove it.");
                            await Task.Delay(500, token);
                            await Notice(request.Nick, "Catches: !catchadd SITE NETWORK #CHANNEL BOT1,BOT2 EVENT WORD1,WORD2 [SECTION]; !catchlist [SITE]; !catchdel ID; !catchtest NETWORK #CHANNEL BOT ANNOUNCEMENT; !announces [SITE]. FiSH CBC keys start cbc:; otherwise ECB.");
                        }
                        else await Notice(request.Nick, "!op: verify linked account and request @; private !login <FTP-user> <password>; !news: project releases; !announces [SITE]; !logout. Use !help setup for site configuration commands.");
                        continue;
                    }
                    if (command == "!logout")
                    {
                        authenticated.TryRemove(request.Prefix, out _);
                        identities.TryRemove(request.Prefix, out _);
                        await Send($"MODE {_settings.Channel} -o {request.Nick}");
                        await Notice(request.Nick, "Login forgotten; removal of @ requested.");
                        continue;
                    }
                    var setup = command is not ("!login" or "!op" or "!news" or "!announces");
                    if (setup && (!request.Private || !_settings.UseTls))
                    {
                        await Notice(request.Nick, "ERROR: Setup commands require a private message and a TLS connection to FluxFTP.");
                        continue;
                    }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var profile = _loadProfile()
                        ?? throw new InvalidOperationException("Admin site unavailable.");
                    if (profile.Protocol is not (TransferProtocol.FtpsExplicit or TransferProtocol.FtpsImplicit) || profile.AllowInvalidCertificate)
                        throw new InvalidOperationException("A verified FTPS site is required.");
                    profile = profile with { Proxy = profile.Proxy ?? new ProxyConfiguration(_global.ProxyType, _global.ProxyHost, _global.ProxyPort,
                        _global.ProxyUsername, _global.ProxyPassword, _global.ProxyDns, _global.ProxyDataConnections) };
                    string? user = null;
                    string? loginPassword = null;
                    if (command == "!login")
                    {
                        if (!_settings.AllowPrivateLogin || !request.Private || !_settings.UseTls || parts.Length != 3 || !IrcProtocol.FtpUser(parts[1]) || parts[2].Any(char.IsControl))
                        {
                            await Notice(request.Nick, "Use a private message: !login <FTP-user> <password>. Private login must be enabled and requires TLS.");
                            continue;
                        }
                        // Credentials only exist for this request, and are never saved or logged.
                        authenticated.TryRemove(request.Prefix, out _);
                        user = parts[1];
                        loginPassword = parts[2];
                    }
                    else
                    {
                        if (request.Account is { } account)
                            user = (_settings.AccountLinks ?? []).SingleOrDefault(link => string.Equals(link.Account, account, StringComparison.OrdinalIgnoreCase))?.FtpUser;
                        if (user is null && authenticated.TryGetValue(request.Prefix, out var prior) && prior.Until > DateTimeOffset.UtcNow)
                            user = prior.User;
                    }
                    if (user is null)
                    {
                        if (!request.Automatic) await Notice(request.Nick, "No verified account link or active login. Identify with NickServ and use !op, or privately !login <FTP-user> <password>.");
                        continue;
                    }
                    if (!IrcProtocol.FtpUser(user)) throw new InvalidDataException();
                    if (!await IrcFtpAccess.VerifyAsync(profile, user, loginPassword, _createSession, timeout.Token))
                    {
                        authenticated.TryRemove(request.Prefix, out _);
                        if (Current(request))
                        {
                            await Send($"MODE {_settings.Channel} -o {request.Nick}");
                            if (!request.Automatic) await Notice(request.Nick, "FTP admin verification failed. SITE USER must expose a Flags field containing 1 or M.");
                        }
                        continue;
                    }
                    if (!Current(request)) continue;
                    if (setup)
                    {
                        var response = await _setupCommand(request.Text, () => Current(request));
                        if (Current(request)) await Notice(request.Nick, response);
                        await Task.Delay(500, token);
                    }
                    else if (command == "!announces")
                    {
                        var siteName = request.Text.Length > command.Length ? request.Text[command.Length..].Trim().Trim('"') : null;
                        var news = _catcher.Recent(string.IsNullOrEmpty(siteName) ? null : siteName);
                        if (news.Count > 0 || parts.Length > 1)
                        {
                            if (news.Count == 0) await Notice(request.Nick, "No captured announcements for that site in this session.");
                            foreach (var item in news)
                            {
                                if (!Current(request)) break;
                                await Notice(request.Nick, $"{item.Site} {item.Event} [{item.Section}] {item.Release}");
                                await Task.Delay(700, token);
                            }
                            continue;
                        }
                        await Notice(request.Nick, "No captured announcements in this session.");
                    }
                    else
                    {
                        if (command == "!login") authenticated[request.Prefix] = (user, DateTimeOffset.UtcNow.AddMinutes(15));
                        await Send($"MODE {_settings.Channel} +o {request.Nick}");
                        if (!request.Automatic) await Notice(request.Nick, "FTP admin verified; @ requested. FluxFTP must be an operator and you must be in the configured channel.");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    // Exception messages can contain server replies or credentials: never relay them.
                    _log("IRC FTP verification/command failed; no access granted.");
                    if (Current(request) && !request.Automatic) await Notice(request.Nick, "FTP verification or command failed. Check the configured site and its SITE USER permissions.");
                }
            }
        }, token);

        try
        {
            var password = _settings.UseZnc ? $"{_settings.ZncUsername}/{_settings.ZncNetwork}:{_settings.Password}" : _settings.Password;
            if (password.Length > 0) await Send($"PASS :{password}");
            await Send("CAP LS 302");
            await Send($"NICK {nick}");
            await Send("USER fluxftp 0 * :FluxFTP IRC control");
            Task<string?>? pendingRead = null;
            while (!token.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextRefresh)
                {
                    if (!registered && DateTimeOffset.UtcNow - started > TimeSpan.FromSeconds(30))
                        throw new IrcConnectionError("IRC registration timed out after 30 seconds. Check server access rules and authentication requirements.");
                    await RefreshChannels();
                    nextRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
                }
                pendingRead ??= reader.ReadLineAsync(token).AsTask();
                var completed = await Task.WhenAny(pendingRead, worker, Task.Delay(TimeSpan.FromSeconds(5), token));
                if (completed == worker)
                {
                    await worker;
                    throw new IOException("IRC command worker stopped.");
                }
                if (completed != pendingRead)
                {
                    token.ThrowIfCancellationRequested();
                    await RefreshChannels();
                    if (DateTimeOffset.UtcNow - lastReceived > TimeSpan.FromMinutes(3)) throw new TimeoutException();
                    if (DateTimeOffset.UtcNow - lastPing > TimeSpan.FromMinutes(1)) { await Send("PING :FluxFTP"); lastPing = DateTimeOffset.UtcNow; }
                    continue;
                }
                var line = await pendingRead; pendingRead = null; lastReceived = DateTimeOffset.UtcNow;
                if (line is null) throw new IrcConnectionError(registered ? "Server closed the established IRC connection." : "Server closed the connection before registration completed. Check server password, TLS/port and server access rules.");
                if (line.Length > 8192) throw new InvalidDataException();
                var m = IrcMessage.Parse(line);
                var p = m.Parameters;
                if (m.Command == "PING" && p.Length > 0) { await Send($"PONG :{p[^1]}"); continue; }
                if (m.Command == "CAP" && p.Length >= 3)
                {
                    if (p[1] == "LS")
                    {
                        foreach (var cap in p[^1].Split(' ')) capabilities.Add(cap.Split('=')[0]);
                        if (p.Length > 3 && p[2] == "*") continue;
                        var wanted = new[] { "account-tag", "account-notify", "extended-join", "server-time", "batch" }.Where(capabilities.Contains).ToArray();
                        if (wanted.Length > 0) await Send("CAP REQ :" + string.Join(' ', wanted));
                        else await Send("CAP END");
                    }
                    if (p[1] is "ACK" or "NAK")
                    {
                        if (p[1] == "ACK") foreach (var cap in p[^1].Split(' ')) acknowledged.Add(cap);
                        await Send("CAP END");
                    }
                    continue;
                }
                if (m.Command == "001")
                {
                    nick = p[0]; registered = true;
                    await RefreshChannels();
                    _log($"IRC [{_settings.NetworkName}] registered as {nick}; channel join requested.");
                    if (_settings.UseZnc) _log("IRC ZNC clients on the same user/network share one IRC nickname. Use a separate ZNC user/network for a separate FluxFTP bot.");
                    if (!acknowledged.Contains("account-tag")) _log("IRC account-tag unavailable: saved links require extended-join or a network supporting account-tag.");
                    if (_settings.UseZnc && !acknowledged.Contains("server-time")) _log("IRC server-time unavailable: ZNC commands blocked to prevent history replay.");
                    continue;
                }
                if (IrcConnectionError.RegistrationReply(m.Command, p.LastOrDefault() ?? "") is { } failure) throw new IrcConnectionError(failure);
                if (m.Command == "JOIN" && p.Length > 0 && IrcProtocol.Same(m.Nick, nick))
                {
                    pendingJoins.Remove(p[0]);
                    _log($"IRC [{_settings.NetworkName}]: {nick} joined {p[0]}.");
                }
                if (m.Command is "403" or "405" or "471" or "473" or "474" or "475" or "476" or "477" or "489")
                {
                    var channelName = p.Length > 1 ? p[1] : "configured channel";
                    pendingJoins.Remove(channelName);
                    var reason = m.Command switch
                    {
                        "403" or "476" => "invalid or unavailable channel", "405" => "too many joined channels",
                        "471" => "channel is full", "473" => "invitation required", "474" => "banned from channel",
                        "475" => "channel key missing or incorrect", "477" => "registered/identified account required",
                        _ => "secure connection required by channel"
                    };
                    _log($"IRC [{_settings.NetworkName}]: JOIN {channelName} rejected ({m.Command}): {reason}.");
                }
                if (m.Command is "401" or "441" or "442" or "482") _log($"IRC server rejected an operation (reply {m.Command}); check channel membership and operator rights.");
                if (m.Command is "NICK" or "QUIT" or "ACCOUNT" or "CHGHOST" or "PART" or "KICK")
                {
                    if (m.Command == "KICK" && p.Length > 1)
                    {
                        foreach (var prefix in identities.Keys.Concat(authenticated.Keys).Distinct().Where(prefix => IrcProtocol.Same(prefix.Split('!')[0], p[1]))) Invalidate(prefix);
                    }
                    else Invalidate(m.Prefix);
                    if (m.Command == "NICK" && IrcProtocol.Same(m.Nick, nick) && p.Length > 0) nick = p[0];
                    if (m.Command == "KICK" && p.Length > 1 && IrcProtocol.Same(p[1], nick)) requestedChannels.Remove(p[0]);
                    if ((m.Command == "KICK" && p.Length > 1 && IrcProtocol.Same(p[1], nick) && IrcProtocol.Same(p[0], _settings.Channel)) ||
                        (m.Command == "PART" && IrcProtocol.Same(m.Nick, nick) && p.Length > 0 && IrcProtocol.Same(p[0], _settings.Channel) && _controlEnabled)) throw new IOException("IRC control channel left.");
                }
                if (!registered || !m.Prefix.Contains('!') || IrcProtocol.Same(m.Nick, nick) || !IrcProtocol.Token(m.Nick)) continue;
                if (!IrcProtocol.IsLive(m, started, _settings.UseZnc)) continue;
                if (m.Command is "PRIVMSG" or "NOTICE" && p.Length == 2)
                {
                    var channel = routing.Channels.FirstOrDefault(c => c.Network.Equals(_settings.NetworkName, StringComparison.OrdinalIgnoreCase) && IrcProtocol.Same(c.Name, p[0]));
                    if (channel is not null)
                    {
                        if (channel.FishKey.Length > 0)
                        {
                            if (!IrcFish.TryDecrypt(p[1], channel.FishKey, out var decrypted)) continue;
                            p[1] = decrypted;
                        }
                        else if (p[1].StartsWith("+OK ", StringComparison.Ordinal)) continue;
                        _catcher.Match(routing, _settings.NetworkName, p[0], m.Nick, p[1], record: true);
                    }
                }
                if (!_controlEnabled) continue;
                string? account = acknowledged.Contains("account-tag") && m.Tags.TryGetValue("account", out var tag) && IrcProtocol.Account(tag) ? tag : null;
                var automatic = m.Command == "JOIN" && p.Length >= 2 && IrcProtocol.Same(p[0], _settings.Channel) && acknowledged.Contains("extended-join");
                if (automatic && IrcProtocol.Account(p[1])) account = p[1];
                var isPrivate = m.Command == "PRIVMSG" && p.Length == 2 && IrcProtocol.Same(p[0], nick);
                if (!automatic && (m.Command != "PRIVMSG" || p.Length != 2 || (!isPrivate && !IrcProtocol.Same(p[0], _settings.Channel)))) continue;
                var text = automatic ? "!op" : p[1];
                var verb = text.Split(' ', 2)[0].ToLowerInvariant();
                if (!verb.StartsWith('!')) continue;
                var setup = verb is not ("!login" or "!op" or "!news" or "!announces" or "!help" or "!logout");
                if (automatic && !( _settings.AccountLinks ?? []).Any(link => string.Equals(link.Account, account, StringComparison.OrdinalIgnoreCase))) continue;
                var now = DateTimeOffset.UtcNow;
                var batchable = setup || verb is "!help" or "!logout";
                if (!batchable && (now < nextRequest || (cooldowns.TryGetValue(m.Prefix, out var until) && now < until))) continue;
                foreach (var expired in cooldowns.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray()) cooldowns.Remove(expired);
                foreach (var expired in authenticated.Where(pair => pair.Value.Until <= now).Select(pair => pair.Key).ToArray()) authenticated.TryRemove(expired, out _);
                if (!batchable) { nextRequest = now.AddSeconds(1); cooldowns[m.Prefix] = now.AddSeconds(10); }
                foreach (var expired in identitySeen.Where(pair => pair.Value < now.AddMinutes(-30)).Select(pair => pair.Key).ToArray())
                { identitySeen.Remove(expired); Invalidate(expired); }
                if (!identities.ContainsKey(m.Prefix) && identities.Count >= 1024) continue;
                identitySeen[m.Prefix] = now;
                var version = identities.GetOrAdd(m.Prefix, _ => Interlocked.Increment(ref epoch));
                if (!requests.Writer.TryWrite(new(m.Prefix, m.Nick, account, text, isPrivate, automatic, version)) && now >= nextBusyNotice)
                {
                    nextBusyNotice = now.AddSeconds(2);
                    await Notice(m.Nick, "BUSY: This command was not queued. Wait for pending replies before retrying this line.");
                }
            }
        }
        finally
        {
            connection.Cancel();
            requests.Writer.TryComplete();
            try { await worker; } catch (OperationCanceledException) { }
        }
    }

    private static string Clean(string text)
    {
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) continue;
            if (Encoding.UTF8.GetByteCount(result.ToString()) + rune.Utf8SequenceLength > 320) break;
            result.Append(rune);
        }
        return result.ToString();
    }

    internal static bool AcceptCertificate(IrcSettings settings, SslPolicyErrors errors) =>
        errors == SslPolicyErrors.None || settings.AllowInvalidCertificate;

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _run;
        _stop.Dispose();
    }

    private sealed record Request(string Prefix, string Nick, string? Account, string Text, bool Private, bool Automatic, long Epoch);
}
