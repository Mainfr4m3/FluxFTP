using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

internal sealed class IrcRoutingCommands(IrcRoutingStore store, IrcCatcher catcher,
    Func<IReadOnlyList<ConnectionProfile>> profiles, string primaryNetwork)
{
    public string Execute(List<string> a)
    {
        try { return Run(a); }
        catch (ArgumentException ex) { return "ERROR: " + ex.Message; }
    }
    private string Run(List<string> a)
    {
        var data = store.Load();
        void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
        void Count(int min, int max, string syntax) => Require(a.Count >= min && a.Count <= max, "Syntax: " + syntax);
        void Network(string name) => Require(name.Equals(primaryNetwork, StringComparison.OrdinalIgnoreCase) || data.Networks.Any(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)), "Unknown network. Primary network: " + primaryNetwork);
        bool Channel(IrcChannel c) => c.Network.Equals(a[1], StringComparison.OrdinalIgnoreCase) && IrcProtocol.Same(c.Name, a[2]);
        switch (a[0].ToLowerInvariant())
        {
            case "!ircnetlist":
                Count(1, 1, "!ircnetlist");
                return "OK: Primary " + primaryNetwork + "; announcement networks: " + string.Join(", ", data.Networks.Select(n => n.Name));
            case "!ircnetadd":
                Count(4, 5, "!ircnetadd NETWORK HOST:PORT tls|plain [NICK]");
                Require(IrcProtocol.Token(a[1]) && a[1].Length <= 32, "Invalid network name.");
                Require(!a[1].Equals(primaryNetwork, StringComparison.OrdinalIgnoreCase) && !data.Networks.Any(n => n.Name.Equals(a[1], StringComparison.OrdinalIgnoreCase)), "Network already exists.");
                Require(a[3] is "tls" or "plain", "Transport must be tls or plain.");
                Require(SiteEndpoint.TryParse(a[2], a[3] == "tls" ? 6697 : 6667, out var endpoint) && Uri.CheckHostName(endpoint.Host) != UriHostNameType.Unknown, "Invalid host/port.");
                var nick = a.Count == 5 ? a[4] : "FluxFTP";
                Require(IrcProtocol.Token(nick), "Invalid nick.");
                store.Update(d => d with { Networks = [.. d.Networks, new(a[1], endpoint.Host, endpoint.Port, a[3] == "tls", nick)] });
                return "OK: Announcement network added; connection will start shortly. Admin commands remain on the primary network.";
            case "!ircnetpass":
                Count(2, 3, "!ircnetpass NETWORK [PASSWORD]"); Network(a[1]);
                Require(!a[1].Equals(primaryNetwork, StringComparison.OrdinalIgnoreCase), "Edit primary network credentials in Global Settings.");
                Require(a.Count == 2 || System.Text.Encoding.UTF8.GetByteCount(a[2]) <= 300, "Password too long.");
                store.Update(d => d with { Networks = d.Networks.Select(n => n.Name.Equals(a[1], StringComparison.OrdinalIgnoreCase) ? n with { Password = a.Count == 3 ? a[2] : "" } : n).ToArray() });
                return "OK: Network password updated; reconnect scheduled.";
            case "!ircnetdel":
                Count(2, 2, "!ircnetdel NETWORK"); Network(a[1]);
                Require(!a[1].Equals(primaryNetwork, StringComparison.OrdinalIgnoreCase), "Disable the primary network in Global Settings.");
                store.Update(d => d with { Networks = d.Networks.Where(n => !n.Name.Equals(a[1], StringComparison.OrdinalIgnoreCase)).ToArray(), Channels = d.Channels.Where(c => !c.Network.Equals(a[1], StringComparison.OrdinalIgnoreCase)).ToArray(), Catches = d.Catches.Where(c => !c.Network.Equals(a[1], StringComparison.OrdinalIgnoreCase)).ToArray() });
                return "OK: Network, channels and catches removed.";
            case "!ircchanlist":
                Count(2, 2, "!ircchanlist NETWORK"); Network(a[1]);
                return "OK: " + string.Join(", ", data.Channels.Where(c => c.Network.Equals(a[1], StringComparison.OrdinalIgnoreCase)).Select(c => c.Name + (c.FishKey.Length > 0 ? " (FiSH)" : "")));
            case "!ircchanadd":
            case "!ircchandel":
            case "!ircchanblow":
            case "!ircchankey":
                var cmd = a[0].ToLowerInvariant();
                Count(3, cmd is "!ircchanblow" or "!ircchankey" ? 4 : 3, cmd + " NETWORK #CHANNEL" + (cmd is "!ircchanblow" or "!ircchankey" ? " [KEY]" : ""));
                Network(a[1]);
                Require(a[2].StartsWith('#') && a[2].Length is > 1 and <= 64 && IrcProtocol.Token(a[2]) && !a[2].Contains(','), "Invalid channel.");
                var existing = data.Channels.FirstOrDefault(Channel);
                if (cmd == "!ircchanadd")
                {
                    Require(existing is null, "Channel already configured.");
                    store.Update(d => d with { Channels = [.. d.Channels, new(a[1], a[2])] });
                }
                else
                {
                    Require(existing is not null, "Use !ircchanadd first.");
                    if (cmd == "!ircchandel") store.Update(d => d with { Channels = d.Channels.Where(c => !Channel(c)).ToArray(), Catches = d.Catches.Where(c => !(c.Network.Equals(a[1], StringComparison.OrdinalIgnoreCase) && IrcProtocol.Same(c.Channel, a[2]))).ToArray() });
                    else
                    {
                        var key = a.Count == 4 ? a[3] : "";
                        if (key.Length > 0 && cmd == "!ircchanblow") IrcFish.ValidateKey(key);
                        if (key.Length > 0 && cmd == "!ircchankey") Require(IrcProtocol.Token(key) && !key.Contains(',') && key.Length <= 100, "Invalid join key.");
                        store.Update(d => d with { Channels = d.Channels.Select(c => !Channel(c) ? c : cmd == "!ircchanblow" ? c with { FishKey = key } : c with { JoinKey = key }).ToArray() });
                    }
                }
                return "OK: Channel settings saved; applied within 5 seconds. The primary control channel stays joined.";
            case "!catchadd":
                Count(7, 8, "!catchadd SITE NETWORK #CHANNEL BOT1,BOT2 EVENT WORD1,WORD2 [SECTION]");
                var site = profiles().SingleOrDefault(p => p.Name.Equals(a[1], StringComparison.OrdinalIgnoreCase));
                Require(site is not null, "Unknown site."); Network(a[2]);
                Require(data.Channels.Any(c => c.Network.Equals(a[2], StringComparison.OrdinalIgnoreCase) && IrcProtocol.Same(c.Name, a[3])), "Add the channel first.");
                var bots = a[4].Split(','); var words = a[6].Split(','); var ev = a[5].ToUpperInvariant();
                Require(bots.All(b => IrcProtocol.Token(b) && b.Length <= 64), "Invalid bot nick list.");
                Require(IrcCatcher.Events.Contains(ev), "Events: " + string.Join(", ", IrcCatcher.Events));
                Require(words.All(w => w.Length is > 0 and <= 64 && !w.Any(char.IsWhiteSpace)), "Use comma-separated match words.");
                var id = data.Catches.Select(c => c.Id).DefaultIfEmpty(0).Max() + 1;
                store.Update(d => d with { Catches = [.. d.Catches, new(id, site!.Name, a[2], a[3], bots, ev, words, a.Count == 8 ? a[7] : "")] });
                return $"OK: Catch {id} added. All words must match; announcements are available through !announces SITE.";
            case "!catchlist":
                Count(1, 2, "!catchlist [SITE]");
                return "OK: " + string.Join("; ", data.Catches.Where(c => a.Count == 1 || c.Site.Equals(a[1], StringComparison.OrdinalIgnoreCase)).Take(8).Select(c => $"{c.Id} {c.Site} {c.Network}/{c.Channel} {c.Event}"));
            case "!catchdel":
                Count(2, 2, "!catchdel ID");
                Require(int.TryParse(a[1], out var removeId) && data.Catches.Any(c => c.Id == removeId), "Unknown catch ID.");
                store.Update(d => d with { Catches = d.Catches.Where(c => c.Id != removeId).ToArray() });
                return "OK: Catch removed.";
            case "!catchtest":
                Count(5, 100, "!catchtest NETWORK #CHANNEL BOT ANNOUNCEMENT TEXT"); Network(a[1]);
                var matches = catcher.Match(data, a[1], a[2], a[3], string.Join(' ', a.Skip(4)));
                return matches.Count == 0 ? "OK: No match." : "OK: " + string.Join("; ", matches.Select(m => $"{m.Site} {m.Event} {m.Section} {m.Release}"));
            default: return "ERROR: Unknown IRC/catch command. Use !help setup.";
        }
    }
}
