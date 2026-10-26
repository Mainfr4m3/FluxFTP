using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
namespace IoFtp.Linux;
internal static class ApiIntegrationChecks
{
    public static async Task Run(MainWindow window, string[] args)
    {
        var count = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; }
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var options = new LinuxApiOptions(true, Port: port, Password: "test-api-password", MaxSpreadJobs: 1, RecheckSeconds: 1, TimeoutMinutes: 1);
        await window.StartApiCheck(options);
        var fingerprint = window.ApiFingerprint;
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256) == window.ApiFingerprint };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(30) };
        async Task<JsonNode> Json(HttpResponseMessage response) { var text = await response.Content.ReadAsStringAsync(); if (!response.IsSuccessStatusCode) throw new Exception($"API HTTP {(int)response.StatusCode}: {text}"); return JsonNode.Parse(text)!; }
        void Auth(string password) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + password)));
        Check((await client.GetAsync("/sites")).StatusCode == HttpStatusCode.Unauthorized, "Missing auth accepted");
        Auth("wrong-password"); Check((await client.GetAsync("/sites")).StatusCode == HttpStatusCode.Unauthorized, "Wrong password accepted");
        Auth(options.Password);
        Check((await Json(await client.GetAsync("/info")))["tls"]!.GetValue<bool>(), "HTTPS info missing");
        foreach (var site in new[] { (Name: "SOURCE", Port: args[1]), (Name: "TARGET", Port: args[2]) })
        {
            var response = await client.PostAsJsonAsync("/sites", new { name = site.Name, addresses = new[] { "127.0.0.1:" + site.Port }, user = "test", password = "test", tls_mode = "NONE", max_logins = 4, max_sim_up = 2, max_sim_down = 2 });
            if (response.StatusCode != HttpStatusCode.Created) throw new Exception("Create site failed: " + response.StatusCode + " " + await response.Content.ReadAsStringAsync());
            Check(true, "Create site");
        }
        Check((await Json(await client.GetAsync("/sites"))).AsArray().Count == 2, "Site name array incompatible");
        var detail = await Json(await client.GetAsync("/sites/SOURCE"));
        Check(detail["addresses"]!.AsArray().Count == 1 && detail["password"] is null, "Site sync format or secret redaction failed");
        Check((await client.PostAsJsonAsync("/sites", new { name = "SOURCE", addresses = new[] { "127.0.0.1:21" } })).StatusCode == HttpStatusCode.Conflict, "Duplicate site accepted");
        Check((await client.PostAsJsonAsync("/sites", new { name = "INVALID", addresses = Array.Empty<string>() })).StatusCode == HttpStatusCode.BadRequest, "Invalid site accepted");
        await Json(await client.PostAsJsonAsync("/sections", new { name = "TV", sites = new[] { new { site = "SOURCE", path = "/releases" }, new { site = "TARGET", path = "/releases" } } }));
        var synced = await Json(await client.GetAsync("/sites/SOURCE"));
        Check(synced["sections"]!.AsArray().Single()!["path"]!.GetValue<string>() == "/releases", "RaceTrade section sync failed");
        using (var patch = new HttpRequestMessage(HttpMethod.Patch, "/sites/SOURCE") { Content = JsonContent.Create(new { priority = "HIGH", needs_pret = true, max_sim_down = 1, list_command = "LIST" }) })
            await Json(await client.SendAsync(patch));
        var patched = await Json(await client.GetAsync("/sites/SOURCE"));
        Check(patched["needs_pret"]!.GetValue<bool>() && patched["priority"]!.GetValue<string>() == "HIGH" && patched["max_sim_down"]!.GetValue<int>() == 1, "Advanced site settings did not roundtrip");
        using (var patch = new HttpRequestMessage(HttpMethod.Patch, "/sites/SOURCE") { Content = JsonContent.Create(new { needs_pret = false, list_command = "AUTO" }) }) await Json(await client.SendAsync(patch));
        Check(new LinuxConfiguration().Load().Sites.Single(site => site.Profile.Name == "SOURCE").Restore().Password == "test", "PATCH erased existing credentials");
        var before = new LinuxConfiguration().Load().Sections.Single(section => section.Name == "TV").SitePaths["SOURCE"];
        Check((await client.PostAsJsonAsync("/sections", new { name = "TV", sites = new[] { new { site = "SOURCE", path = "/changed" }, new { site = "UNKNOWN", path = "/invalid" } } })).StatusCode == HttpStatusCode.NotFound, "Invalid section mapping accepted");
        Check(new LinuxConfiguration().Load().Sections.Single(section => section.Name == "TV").SitePaths["SOURCE"] == before, "Failed section mutation was partially applied");
        var raw = await Json(await client.PostAsJsonAsync("/raw", new { command = "NOOP", sites = new[] { "SOURCE" } }));
        Check(raw["successes"]!.AsArray().Count == 1 && raw["failures"]!.AsArray().Count == 0, "Raw response wrapper incompatible");
        Check((await client.GetAsync("/path?site=SOURCE&path=%2Freleases")).IsSuccessStatusCode, "Path endpoint failed");
        Check((await client.PostAsJsonAsync("/spreadjobs", new { section = "TV", name = "../escape", sites = new[] { "SOURCE", "TARGET" } })).StatusCode == HttpStatusCode.BadRequest, "Release traversal accepted");
        async Task<JsonNode> Wait(string route, string status)
        {
            var deadline = DateTime.UtcNow.AddSeconds(35);
            while (DateTime.UtcNow < deadline)
            {
                var value = await Json(await client.GetAsync(route)); var actual = value["status"]!.GetValue<string>();
                if (actual == status) return value;
                if (actual is "FAILED" or "ABORTED") throw new Exception("Job failed unexpectedly: " + value.ToJsonString());
                await Task.Delay(150);
            }
            throw new TimeoutException("Job did not reach " + status + ": " + route);
        }
        var started = await Json(await client.PostAsJsonAsync("/transferjobs", new { name = "Test.Release", src_site = "SOURCE", src_section = "TV", dst_site = "TARGET", dst_section = "TV" }));
        Check(started["state"]!.GetValue<string>() == "STARTED", "RaceTrade transfer acceptance failed");
        await Wait("/transferjobs/Test.Release", "DONE");
        var sourceRoot = args[3]; var targetRoot = args[4];
        Check(File.ReadAllBytes(Path.Combine(sourceRoot, "releases/Test.Release/fixture.bin")).SequenceEqual(File.ReadAllBytes(Path.Combine(targetRoot, "releases/Test.Release/fixture.bin"))), "API FXP payload mismatch");
        Check(File.ReadAllBytes(Path.Combine(sourceRoot, "releases/Test.Release/Subs/sub.bin")).SequenceEqual(File.ReadAllBytes(Path.Combine(targetRoot, "releases/Test.Release/Subs/sub.bin"))), "Recursive API payload mismatch");
        Check((await Json(await client.GetAsync("/transferjobs"))).AsArray().Count == 1, "Transfer collection missing");
        var growing = Path.Combine(sourceRoot, "releases/Growing.Release"); Directory.CreateDirectory(growing);
        await File.WriteAllBytesAsync(Path.Combine(growing, "first.bin"), new byte[32768]);
        await Json(await client.PostAsJsonAsync("/spreadjobs", new { name = "Growing.Release", section = "TV", sites = new[] { "SOURCE", "TARGET" }, sites_dlonly = new[] { "SOURCE" } }));
        Check((await client.PostAsJsonAsync("/spreadjobs", new { name = "Growing.Release", section = "TV", sites = new[] { "SOURCE", "TARGET" } })).StatusCode == HttpStatusCode.Conflict, "Duplicate active race accepted");
        Check((await client.PostAsJsonAsync("/spreadjobs", new { name = "Other.Release", section = "TV", sites = new[] { "SOURCE", "TARGET" } })).StatusCode == HttpStatusCode.TooManyRequests, "Active job limit ignored");
        var firstTarget = Path.Combine(targetRoot, "releases/Growing.Release/first.bin");
        var firstDeadline = DateTime.UtcNow.AddSeconds(20); while (!File.Exists(firstTarget) && DateTime.UtcNow < firstDeadline) await Task.Delay(100);
        Check(File.Exists(firstTarget), "Growing race did not transfer initial file");
        Check((await Json(await client.GetAsync("/spreadjobs/Growing.Release")))["status"]!.GetValue<string>() == "RUNNING", "Growing release prematurely completed");
        var extra = System.Security.Cryptography.RandomNumberGenerator.GetBytes(17001); await File.WriteAllBytesAsync(Path.Combine(growing, "later.bin"), extra);
        Directory.CreateDirectory(Path.Combine(growing, "[100%]-COMPLETE"));
        var finished = await Wait("/spreadjobs/Growing.Release", "DONE");
        Check(finished["sites_incomplete"]!.AsArray().Count == 0 && File.ReadAllBytes(Path.Combine(targetRoot, "releases/Growing.Release/later.bin")).SequenceEqual(extra), "Growing race final verification failed");
        await Json(await client.PostAsJsonAsync("/spreadjobs", new { name = "Waiting.Release", section = "TV", sites = new[] { "SOURCE", "TARGET" } }));
        await Json(await client.PostAsync("/spreadjobs/Waiting.Release/abort", null));
        Check((await Json(await client.GetAsync("/spreadjobs/Waiting.Release")))["status"]!.GetValue<string>() == "ABORTED", "Abort not reflected");
        var settings = new LinuxConfiguration().Load(); Check(settings.Options.Api?.Password == options.Password, "API settings did not persist");
        Check(!File.ReadAllText(Path.Combine(SecretStore.DirectoryPath, "state.enc")).Contains(options.Password, StringComparison.Ordinal), "API password stored in plain text");
        await window.StartApiCheck(options);
        Check(window.ApiFingerprint == fingerprint, "TLS certificate changed unnecessarily on restart");
        var blocker = new TcpListener(IPAddress.Loopback, 0); blocker.Start();
        var blockedPort = ((IPEndPoint)blocker.LocalEndpoint).Port; var rejected = false;
        try { await window.StartApiCheck(options with { Port = blockedPort }); } catch { rejected = true; } finally { blocker.Stop(); }
        Check(rejected && new LinuxConfiguration().Load().Options.Api!.Port == port, "Failed listener start changed saved API settings");
        Check((await client.GetAsync("/info")).IsSuccessStatusCode && window.ApiFingerprint == fingerprint, "Previous API was not restored after bind failure");
        Console.WriteLine($"PASS: {count} Linux HTTPS/RaceTrade checks: auth, site/section sync, raw commands, real FXP, recursive files, growing spreadjobs, limits, duplicates, abort, encrypted settings and stable TLS.");
    }
}
