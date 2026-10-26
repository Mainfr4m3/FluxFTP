using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
namespace IoFtp.Linux;
internal sealed partial class LinuxApiServer : IAsyncDisposable
{
    private readonly LinuxConfiguration _configuration;
    private readonly Func<LinuxApiFile, Task<Guid>> _queue;
    private readonly Func<IEnumerable<Guid>, Task<List<LinuxApiJob>>> _jobs;
    private readonly Func<Guid, Task> _cancel;
    private readonly Action<string> _log;
    private readonly Action _changed;
    private WebApplication? _app;
    private LinuxApiOptions _options = new();
    private X509Certificate2? _certificate;
    private readonly CancellationTokenSource _stop = new();
    public string Endpoint { get; private set; } = "Disabled";
    public string Fingerprint => _certificate?.GetCertHashString(HashAlgorithmName.SHA256) ?? "";
    public LinuxApiServer(LinuxConfiguration configuration, Func<LinuxApiFile, Task<Guid>> queue,
        Func<IEnumerable<Guid>, Task<List<LinuxApiJob>>> jobs, Func<Guid, Task> cancel, Action<string> log, Action? changed = null)
    { _configuration = configuration; _queue = queue; _jobs = jobs; _cancel = cancel; _log = log; _changed = changed ?? (() => { }); }
    public async Task StartAsync(LinuxApiOptions options)
    {
        options.Validate(); _options = options;
        if (!options.Enabled) return;
        _certificate = LoadCertificate();
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(3));
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.WriteIndented = true);
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestBodySize = 1024 * 1024;
            server.Listen(IPAddress.Parse(options.Address), options.Port, listen => listen.UseHttps(_certificate));
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (!Authorized(context, options.Password))
            {
                context.Response.StatusCode = 401; context.Response.Headers.WWWAuthenticate = "Basic realm=\"FluxFTP\"";
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" }); return;
            }
            try { await next(); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or BadHttpRequestException or KeyNotFoundException)
            { if (!context.Response.HasStarted) { context.Response.StatusCode = ex is BadHttpRequestException bad ? bad.StatusCode : ex is KeyNotFoundException ? 404 : 400; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); } }
            catch (Exception ex)
            { _log("API request failed: " + ex.GetType().Name); if (!context.Response.HasStarted) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "Operation failed; check the local FTP log." }); } }
        });
        app.MapGet("/info", () => Results.Json(new { name = "FluxFTP Linux", version = "0.1.0-preview.3", api = "cbftp-compatible", tls = true, udp = false }));
        app.MapGet("/sites", () => Results.Json(Sites().Select(site => site.Name)));
        app.MapGet("/sites/{name}", (string name) => Results.Json(ApiSite(FindSite(name))));
        app.MapPost("/sites", (Delegate)(async (HttpContext context) => await SaveSite(null, context)));
        app.MapPatch("/sites/{name}", async (string name, HttpContext context) => await SaveSite(name, context));
        app.MapDelete("/sites/{name}", (string name) =>
        {
            var site = FindSite(name); _configuration.Update(state => state with { Sites = state.Sites.Where(saved => saved.Profile.Id != site.Id).ToList() });
            _changed(); return Results.Ok(new { deleted = name });
        });
        app.MapGet("/sections", () => Results.Json(_configuration.Load().Sections.Select(section => section.Name)));
        app.MapGet("/sections/{name}", (string name) => Results.Json(ApiSection(FindSection(name))));
        app.MapPost("/sections", (Delegate)(async (HttpContext context) => await SaveSection(null, null, context)));
        app.MapPatch("/sections/{name}", async (string name, HttpContext context) => await SaveSection(name, null, context));
        app.MapDelete("/sections/{name}", (string name) => { _ = FindSection(name); _configuration.Update(state => state with { Sections = state.Sections.Where(section => !Same(section.Name, name)).ToList() }); _changed(); return Results.Ok(new { deleted = name }); });
        app.MapGet("/sites/{site}/sections", (string site) => { _ = FindSite(site); return Results.Json(SectionsFor(site)); });
        app.MapGet("/sites/{site}/sections/{name}", (string site, string name) => Results.Json(new { name, path = Resolve(FindSite(site), name, null) }));
        app.MapPost("/sites/{site}/sections", async (string site, HttpContext context) => await SaveSection(null, site, context));
        app.MapPatch("/sites/{site}/sections/{name}", async (string site, string name, HttpContext context) => await SaveSection(name, site, context));
        app.MapDelete("/sites/{site}/sections/{name}", (string site, string name) =>
        {
            _ = FindSite(site); _ = FindSection(name);
            _configuration.Update(state => state with { Sections = state.Sections.Select(section => Same(section.Name, name) ? section with { SitePaths = section.SitePaths.Where(pair => !Same(pair.Key, site)).ToDictionary() } : section).ToList() });
            _changed(); return Results.Ok(new { deleted = name, site });
        });
        app.MapGet("/path", async (string site, string path, HttpContext context) =>
        {
            var profile = FindSite(site); await using var remote = await Open(profile, context.RequestAborted);
            return Results.Json((await remote.Session.ListAsync(Resolve(profile, null, path), remote.Token)).Select(entry => new { name = entry.Name, path = entry.FullPath, dir = entry.IsDirectory, size = entry.Size, modified = entry.ModifiedAt }));
        });
        app.MapGet("/file", async (string site, string path, HttpContext context) =>
        {
            var profile = FindSite(site); await using var remote = await Open(profile, context.RequestAborted);
            var resolved = Resolve(profile, null, path); var size = await remote.Session.GetSizeAsync(resolved, remote.Token);
            return size is null ? Results.NotFound(new { error = "File not found" }) : Results.Json(new { site, path = resolved, size });
        });
        app.MapPost("/raw", async (LinuxRawRequest request, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Command) || request.Command.Any(char.IsControl) || request.Command.Length > 4096) throw new ArgumentException("A single raw command is required.");
            var sites = request.AllSites ? Sites().Select(site => site.Name).ToArray() : request.Sites ?? [];
            if (sites.Length is 0 or > 128) throw new ArgumentException("Specify sites or sites_all (maximum 128 sites).");
            var successes = new List<object>(); var failures = new List<object>();
            foreach (var name in sites.Distinct(StringComparer.OrdinalIgnoreCase))
                try
                {
                    var profile = FindSite(name); await using var remote = await Open(profile, context.RequestAborted);
                    if (request.Path is not null || request.Section is not null)
                    { var cwd = await remote.Session.ExecuteCommandAsync("CWD " + Resolve(profile, request.Section, request.Path), remote.Token); if (cwd.StatusCode >= 400) throw new IOException("Raw command directory unavailable"); }
                    var response = await remote.Session.ExecuteCommandAsync(request.Command, remote.Token);
                    if (response.StatusCode >= 400) failures.Add(new { name, reason = response.Message, status = "failed" });
                    else successes.Add(new { name, result = response.Message, status = "success" });
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { failures.Add(new { name, reason = "Command failed: " + ex.GetType().Name, status = "failed" }); }
            return Results.Json(new { successes, failures, dupes = Array.Empty<object>() });
        });
        app.MapGet("/transferjobs", () => Results.Json(GroupSnapshots(false)));
        app.MapGet("/transferjobs/{name}", (string name) => Results.Json(GroupSnapshot(FindGroup(name, false))));
        app.MapPost("/transferjobs", (LinuxTransferRequest request) => StartTransfer(request));
        app.MapPost("/transferjobs/{name}/abort", async (string name) => await Abort(name, false));
        app.MapDelete("/transferjobs/{name}", async (string name) => await Abort(name, false));
        app.MapPost("/transferjobs/{name}/reset", (string name) => Reset(name));
        app.MapGet("/spreadjobs", (string? status) => Results.Json(GroupSnapshots(true).Where(group => string.IsNullOrEmpty(status) || Same(group.Status, status))));
        app.MapGet("/spreadjobs/{name}", (string name) => Results.Json(GroupSnapshot(FindGroup(name, true))));
        app.MapPost("/spreadjobs", (LinuxSpreadRequest request) => StartSpread(request));
        app.MapPost("/spreadjobs/{name}/abort", async (string name) => await Abort(name, true));
        app.MapDelete("/spreadjobs/{name}", async (string name) => await Abort(name, true));
        try { await app.StartAsync(); } catch { await app.DisposeAsync(); throw; }
        _app = app; Endpoint = $"https://{options.Address}:{options.Port}";
        _log("RaceTrade API started: " + Endpoint);
    }
    private static bool Authorized(HttpContext context, string password)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) || header.Length > 1024) return false;
        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..])); var colon = value.IndexOf(':');
            return colon >= 0 && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(value[(colon + 1)..])), SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        }
        catch (FormatException) { return false; }
    }
    private X509Certificate2 LoadCertificate()
    {
        var addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().SelectMany(network => network.GetIPProperties().UnicastAddresses).Select(item => item.Address).Distinct().OrderBy(address => address.ToString()).ToArray();
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Dns.GetHostName() + string.Join(",", addresses.Select(address => address.ToString())))))[..16];
        var path = System.IO.Path.Combine(SecretStore.DirectoryPath, "api-certificate-" + identity + ".enc");
        if (File.Exists(path))
        {
            var existing = new X509Certificate2(Convert.FromBase64String(SecretStore.Unprotect(File.ReadAllText(path))));
            if (existing.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(1)) return existing;
            existing.Dispose();
        }
        // Certificates remain stable across restarts, and rotate when local network identities change.
        using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=FluxFTP Linux", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); san.AddDnsName(Dns.GetHostName()); san.AddIpAddress(IPAddress.Loopback);
        foreach (var address in addresses)
            if (!IPAddress.IsLoopback(address)) san.AddIpAddress(address);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
        var pfx = certificate.Export(X509ContentType.Pfx);
        SecretStore.WritePrivate(path, SecretStore.Protect(Convert.ToBase64String(pfx)));
        return new X509Certificate2(pfx);
    }
    private List<ConnectionProfile> Sites()
    {
        var state = _configuration.Load(); return state.Sites.Select(saved => { var site = saved.Restore(); return site with { Proxy = site.Proxy ?? state.Options.Proxy }; }).ToList();
    }
    private ConnectionProfile FindSite(string name) => Sites().FirstOrDefault(site => Same(site.Name, name)) ?? throw new KeyNotFoundException("Site not found: " + name);
    private LinuxSection FindSection(string name) => _configuration.Load().Sections.FirstOrDefault(section => Same(section.Name, name)) ?? throw new KeyNotFoundException("Section not found: " + name);
    private static bool Same(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    private static string Clean(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || path.Contains('\\') || path.Split('/').Any(part => part is "." or "..")) throw new ArgumentException("Invalid remote path.");
        return path;
    }
    private string Resolve(ConnectionProfile profile, string? section, string? path)
    {
        if (!string.IsNullOrWhiteSpace(section)) return Clean(FindSection(section).SitePaths.FirstOrDefault(pair => Same(pair.Key, profile.Name)).Value ?? throw new KeyNotFoundException("Site section not configured: " + profile.Name));
        if (!string.IsNullOrEmpty(path)) return path.StartsWith('/') ? Clean(path) : Resolve(profile, path, null);
        return Clean(profile.EffectiveOptions.BasePath);
    }
    private object[] SectionsFor(string site) => _configuration.Load().Sections.Select(section => new { name = section.Name, path = section.SitePaths.FirstOrDefault(pair => Same(pair.Key, site)).Value }).Where(section => section.path is not null).Cast<object>().ToArray();
    private object ApiSection(LinuxSection section) => new { name = section.Name, sites = section.SitePaths.Select(pair => new { site = pair.Key, path = pair.Value }) };
    private object ApiSite(ConnectionProfile site) => new { name = site.Name, description = site.Description, addresses = site.EffectiveAddresses.Select(address => address.ToString()), user = site.Username,
        base_path = site.EffectiveOptions.BasePath, tls_mode = site.Protocol switch { TransferProtocol.FtpsExplicit => "AUTH_TLS", TransferProtocol.FtpsImplicit => "IMPLICIT", TransferProtocol.Sftp => "SFTP", _ => "NONE" },
        max_logins = site.EffectiveOptions.MaxSlots, max_sim_up = site.EffectiveOptions.MaxUploadSlots, max_sim_down = site.EffectiveOptions.MaxDownloadSlots,
        priority = site.EffectiveOptions.Priority < 0 ? "LOW" : site.EffectiveOptions.Priority > 0 ? "HIGH" : "NORMAL",
        needs_pret = site.EffectiveOptions.NeedsPret, pret = site.EffectiveOptions.NeedsPret, cepr_supported = site.EffectiveOptions.CeprSupported,
        use_xdupe = site.EffectiveOptions.UseXdupe, force_binary = site.EffectiveOptions.ForceBinaryMode,
        list_command = site.ListingMode == DirectoryListingMode.ListOnly ? "LIST" : site.ListingMode == DirectoryListingMode.Auto ? "AUTO" : "STAT_L",
        fxp_protection = site.EffectiveOptions.FxpProtection == FxpProtectionMode.Clear ? "CLEAR" : "AUTO",
        fxp_data_role = site.EffectiveOptions.FxpDataRole.ToString().ToUpperInvariant(), disabled = false, sections = SectionsFor(site.Name) };
    private async Task<IResult> SaveSite(string? name, HttpContext context)
    {
        var input = await context.Request.ReadFromJsonAsync<JsonObject>(context.RequestAborted) ?? throw new ArgumentException("JSON is required.");
        var old = name is null ? null : FindSite(name); var proposed = input["name"]?.GetValue<string>() ?? name ?? throw new ArgumentException("name is required");
        if (string.IsNullOrWhiteSpace(proposed) || proposed.Any(char.IsControl)) throw new ArgumentException("Invalid site name.");
        var site = old ?? new ConnectionProfile(Guid.NewGuid(), proposed, "", 21, "", TransferProtocol.FtpsExplicit);
        var addresses = input["addresses"]?.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        if (addresses is { Length: 0 }) throw new ArgumentException("addresses must not be empty");
        if (addresses is not null)
        {
            if (!SiteEndpoint.TryParse(addresses[0], site.Port, out var endpoint)) throw new ArgumentException("Invalid site address");
            site = site with { Host = endpoint.Host, Port = endpoint.Port, AlternateAddresses = string.Join(' ', addresses.Skip(1)) };
        }
        if (string.IsNullOrWhiteSpace(site.Host)) throw new ArgumentException("addresses is required");
        var protocol = input["tls_mode"]?.GetValue<string>()?.ToUpperInvariant() switch { null => site.Protocol, "AUTH_TLS" or "EXPLICIT" => TransferProtocol.FtpsExplicit, "IMPLICIT" => TransferProtocol.FtpsImplicit, "NONE" => TransferProtocol.Ftp, "SFTP" => TransferProtocol.Sftp, _ => throw new ArgumentException("Invalid tls_mode") };
        int Limit(string key, int fallback) { var value = input[key]?.GetValue<int>() ?? fallback; return value is >= 0 and <= 2048 ? value : throw new ArgumentException("Invalid " + key); }
        bool Flag(string name, bool fallback, string? alias = null) => input[name]?.GetValue<bool>() ?? (alias is not null ? input[alias]?.GetValue<bool>() : null) ?? fallback;
        string List(string name, string fallback) => input[name] is JsonArray values ? string.Join(';', values.Select(value => value!.GetValue<string>())) : fallback;
        var priority = site.EffectiveOptions.Priority;
        if (input["priority"] is JsonValue value)
        {
            if (value.TryGetValue<int>(out var number)) priority = number;
            else if (value.TryGetValue<string>(out var text)) priority = text.ToUpperInvariant() switch { "LOW" => -10, "HIGH" => 10, "NORMAL" => 0, _ => int.TryParse(text, out var parsed) ? parsed : throw new ArgumentException("Invalid priority") };
        }
        var options = site.EffectiveOptions with { MaxSlots = Limit("max_logins", site.EffectiveOptions.MaxSlots), MaxUploadSlots = Limit("max_sim_up", site.EffectiveOptions.MaxUploadSlots), MaxDownloadSlots = Limit("max_sim_down", site.EffectiveOptions.MaxDownloadSlots), BasePath = Clean(input["base_path"]?.GetValue<string>() ?? site.EffectiveOptions.BasePath), Priority = priority,
            NeedsPret = Flag("needs_pret", site.EffectiveOptions.NeedsPret, "pret"), CeprSupported = Flag("cepr_supported", site.EffectiveOptions.CeprSupported, "cepr"), UseXdupe = Flag("use_xdupe", site.EffectiveOptions.UseXdupe, "xdupe"),
            ForceBinaryMode = Flag("force_binary", site.EffectiveOptions.ForceBinaryMode, "force_binary_mode"),
            BlockTransfersFrom = List("except_source_sites", site.EffectiveOptions.BlockTransfersFrom), BlockTransfersTo = List("except_target_sites", site.EffectiveOptions.BlockTransfersTo), Affils = List("affils", site.EffectiveOptions.Affils),
            FxpProtection = input["fxp_protection"]?.GetValue<string>()?.ToUpperInvariant() switch { null => site.EffectiveOptions.FxpProtection, "CLEAR" => FxpProtectionMode.Clear, "AUTO" or "AUTOSECURE" => FxpProtectionMode.AutoSecure, _ => throw new ArgumentException("Invalid fxp_protection") },
            FxpDataRole = input["fxp_data_role"]?.GetValue<string>()?.ToUpperInvariant() switch { null => input["broken_pasv"]?.GetValue<bool>() == true ? FxpDataRole.Active : site.EffectiveOptions.FxpDataRole, "PASV" or "PASSIVE" => FxpDataRole.Passive, "PORT" or "ACTIVE" => FxpDataRole.Active, "AUTO" => FxpDataRole.Auto, _ => throw new ArgumentException("Invalid fxp_data_role") } };
        var listing = input["list_command"]?.GetValue<string>()?.ToUpperInvariant() switch { null => site.ListingMode, "AUTO" => DirectoryListingMode.Auto, "LIST" => DirectoryListingMode.ListOnly, "STAT_L" => DirectoryListingMode.StatThenList, _ => throw new ArgumentException("Invalid list_command") };
        site = site with { Name = proposed, Username = input["user"]?.GetValue<string>() ?? site.Username, Password = input["password"]?.GetValue<string>() ?? site.Password, Protocol = protocol, ListingMode = listing, Options = options, Description = input["description"]?.GetValue<string>() ?? site.Description };
        var attached = input["sections"] is JsonArray sectionValues ? sectionValues.Select(section => (Name: section?["name"]?.GetValue<string>() ?? "", Path: section?["path"]?.GetValue<string>() ?? "")).ToArray() : [];
        foreach (var section in attached) { if (string.IsNullOrWhiteSpace(section.Name) || section.Name.Any(char.IsControl)) throw new ArgumentException("Invalid section name"); _ = Clean(section.Path); }
        var saved = site;
        var conflict = false;
        _configuration.Update(state =>
        {
            if (state.Sites.Any(item => item.Profile.Id != saved.Id && Same(item.Profile.Name, proposed))) { conflict = true; return state; }
            var sections = state.Sections.Select(section => new LinuxSection(section.Name, new Dictionary<string, string>(section.SitePaths, StringComparer.OrdinalIgnoreCase))).ToList();
            if (old is not null && !Same(old.Name, proposed))
                foreach (var section in sections)
                    if (section.SitePaths.Remove(old.Name, out var oldPath)) section.SitePaths[proposed] = oldPath;
            foreach (var entry in attached)
            {
                var section = sections.FirstOrDefault(item => Same(item.Name, entry.Name));
                if (section is null) { section = new(entry.Name, new(StringComparer.OrdinalIgnoreCase)); sections.Add(section); }
                section.SitePaths[proposed] = entry.Path;
            }
            return state with { Sites = state.Sites.Where(item => item.Profile.Id != saved.Id).Append(SavedSite.Capture(saved)).ToList(), Sections = sections };
        });
        if (conflict) return Results.Conflict(new { error = "Site name already exists" });
        _changed(); return name is null ? Results.Created("/sites/" + Uri.EscapeDataString(proposed), ApiSite(site)) : Results.Json(ApiSite(site));
    }
    private async Task<IResult> SaveSection(string? name, string? site, HttpContext context)
    {
        var input = await context.Request.ReadFromJsonAsync<JsonObject>(context.RequestAborted) ?? throw new ArgumentException("JSON is required");
        name ??= input["name"]?.GetValue<string>() ?? throw new ArgumentException("name is required");
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) throw new ArgumentException("Invalid section name");
        if (site is not null) { _ = FindSite(site); SaveSectionValue(name, site, input["path"]?.GetValue<string>() ?? throw new ArgumentException("path is required")); }
        else if (input["sites"] is JsonArray mappings)
        {
            var paths = mappings.Select(mapping => (Site: mapping?["site"]?.GetValue<string>() ?? throw new ArgumentException("site is required"), Path: Clean(mapping?["path"]?.GetValue<string>() ?? ""))).ToArray();
            foreach (var entry in paths) _ = FindSite(entry.Site);
            var sectionName = name;
            _configuration.Update(state =>
            {
                var sections = state.Sections.ToList(); var index = sections.FindIndex(section => Same(section.Name, sectionName));
                var values = index < 0 ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(sections[index].SitePaths, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in paths) values[entry.Site] = entry.Path;
                if (index < 0) sections.Add(new(sectionName, values)); else sections[index] = sections[index] with { SitePaths = values };
                return state with { Sections = sections };
            });
        }
        else _configuration.Update(state => state.Sections.Any(section => Same(section.Name, name)) ? state : state with { Sections = state.Sections.Append(new LinuxSection(name, [])).ToList() });
        _changed(); return Results.Json(ApiSection(FindSection(name)));
    }
    private void SaveSectionValue(string name, string site, string path)
    {
        path = Clean(path);
        _configuration.Update(state =>
        {
            var sections = state.Sections.ToList(); var index = sections.FindIndex(section => Same(section.Name, name));
            var values = index < 0 ? new Dictionary<string, string>() : sections[index].SitePaths.Where(pair => !Same(pair.Key, site)).ToDictionary();
            values[site] = path;
            if (index < 0) sections.Add(new(name, values)); else sections[index] = sections[index] with { SitePaths = values };
            return state with { Sections = sections };
        });
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_app is not null)
        {
            using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _app.StopAsync(stopping.Token); } catch (OperationCanceledException) { }
            await _app.DisposeAsync(); _app = null;
        }
        ApiGroup[] groups; lock (_groupGate) groups = _groups.Values.ToArray();
        foreach (var group in groups) group.Cancellation.Cancel();
        try { await Task.WhenAll(groups.Select(group => group.Worker ?? Task.CompletedTask)).WaitAsync(TimeSpan.FromSeconds(15)); } catch (TimeoutException) { _log("API jobs are still stopping."); }
        _certificate?.Dispose(); _stop.Dispose();
    }
}
