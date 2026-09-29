using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IoFtp.Desktop.Services;

internal sealed record IrcNetwork(string Name, string Host, int Port = 6697, bool Tls = true, string Nick = "FluxFTP", string Password = "");
internal sealed record IrcChannel(string Network, string Name, string FishKey = "", string JoinKey = "");
internal sealed record IrcCatch(int Id, string Site, string Network, string Channel, string[] BotNicks, string Event, string[] Words, string Section = "");
internal sealed record IrcRouting(IrcNetwork[] Networks, IrcChannel[] Channels, IrcCatch[] Catches)
{
    public static IrcRouting Empty => new([], [], []);
}
internal sealed class IrcRoutingStore(string? path = null)
{
    private static readonly object Gate = new();
    private readonly string _path = path ?? Path.Combine(AppContext.BaseDirectory, "FluxFTP-irc-routing.json");
    public IrcRouting Load()
    {
        lock (Gate)
        {
            if (!File.Exists(_path)) return IrcRouting.Empty;
            var value = JsonSerializer.Deserialize<IrcRouting>(File.ReadAllText(_path)) ?? throw new InvalidDataException("Invalid IRC routing file.");
            if (value.Networks is null || value.Channels is null || value.Catches is null) throw new InvalidDataException("Invalid IRC routing file.");
            return value with
            {
                Networks = value.Networks.Select(n => n with { Password = Unprotect(n.Password) }).ToArray(),
                Channels = value.Channels.Select(c => c with { FishKey = Unprotect(c.FishKey), JoinKey = Unprotect(c.JoinKey) }).ToArray()
            };
        }
    }
    public void Update(Func<IrcRouting, IrcRouting> update)
    {
        lock (Gate)
        {
            var value = update(Load());
            if (value.Networks.Length > 8 || value.Channels.Length > 64 || value.Catches.Length > 512) throw new InvalidDataException("IRC configuration limit reached.");
            var protectedValue = value with
            {
                Networks = value.Networks.Select(n => n with { Password = Protect(n.Password) }).ToArray(),
                Channels = value.Channels.Select(c => c with { FishKey = Protect(c.FishKey), JoinKey = Protect(c.JoinKey) }).ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(protectedValue, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
    }
    private static string Protect(string text) => text.Length == 0 ? "" : "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));
    private static string Unprotect(string text) => text.Length == 0 ? "" : text.StartsWith("dpapi:", StringComparison.Ordinal)
        ? Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text[6..]), null, DataProtectionScope.CurrentUser))
        : throw new InvalidDataException("IRC keys must be configured through FluxFTP.");
}
