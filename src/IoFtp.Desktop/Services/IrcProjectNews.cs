using System.Net.Http;
using System.Text.Json;

namespace IoFtp.Desktop.Services;

internal sealed class IrcProjectNews
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _gate = new(1);
    private string[]? _cached;
    private DateTimeOffset _expires;
    public async Task<IReadOnlyList<string>> GetAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow < _expires) return _cached;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Mainfr4m3/FluxFTP/releases/latest");
                request.Headers.UserAgent.ParseAdd("FluxFTP-IRC/1.0");
                using var response = await Client.SendAsync(request, token);
                response.EnsureSuccessStatusCode();
                var lines = Parse(await response.Content.ReadAsStringAsync(token));
                _cached = lines.Length > 0 ? lines : ["FluxFTP releases: https://github.com/Mainfr4m3/FluxFTP/releases"];
                _expires = DateTimeOffset.UtcNow.AddMinutes(10);
                return _cached;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !token.IsCancellationRequested)
            {
                _expires = DateTimeOffset.UtcNow.AddMinutes(1);
                return _cached ??= ["Project news unavailable right now. FluxFTP releases: https://github.com/Mainfr4m3/FluxFTP/releases"];
            }
        }
        finally { _gate.Release(); }
    }
    internal static string[] Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var r = document.RootElement;
        if (r.GetProperty("draft").GetBoolean() || r.GetProperty("prerelease").GetBoolean()) return [];
        {
            var tag = r.GetProperty("tag_name").GetString() ?? "Release";
            var date = r.GetProperty("published_at").GetDateTimeOffset().ToString("yyyy-MM-dd HH:mm 'UTC'");
            var kind = r.GetProperty("prerelease").GetBoolean() ? "test build" : "release";
            return [$"({date}) FluxFTP {kind} {tag}: https://github.com/Mainfr4m3/FluxFTP/releases/tag/{Uri.EscapeDataString(tag)}"];
        }
    }
}
