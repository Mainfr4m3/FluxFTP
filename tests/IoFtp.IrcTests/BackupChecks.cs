using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IoFtp.Desktop.Services;

internal static class BackupChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluxFTP-backup-test-" + Guid.NewGuid().ToString("N"));
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        try
        {
            var app = Path.Combine(root, "source-app"); var user = Path.Combine(root, "source-user");
            var newApp = Path.Combine(root, "restored-app"); var newUser = Path.Combine(root, "restored-user");
            Directory.CreateDirectory(app); Directory.CreateDirectory(user); Directory.CreateDirectory(newApp); Directory.CreateDirectory(newUser);
            const string secret = "PRIVATE_TEST_PASSWORD_åö✓";
            var protectedSecret = "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));
            var linked = Path.Combine(root, "rushopt.ini"); File.WriteAllText(linked, "[normal]\ncompleteflag=COMPLETE");
            File.WriteAllText(Path.Combine(app, "FluxFTP-sites.ini"), "[site:test]\nPassword=" + protectedSecret);
            File.WriteAllText(Path.Combine(app, "FluxFTP-visionary-race.json"), JsonSerializer.Serialize(new { RushOptionsPath = linked }));
            File.WriteAllText(Path.Combine(user, "settings.json"), JsonSerializer.Serialize(new
            {
                ApiPassword = protectedSecret,
                Irc = new { Password = protectedSecret, Enabled = true },
                Rules = new object?[] { new { Pattern = "*.bad", Priority = 2 }, new[] { "*.mkv", "*.jpg" }, null },
                Layout = new { Left = new { Visible = new[] { "Name", "Size" }, Width = 123.5 } }
            }));
            File.WriteAllText(Path.Combine(user, "total-statistics.json"), "{\"Transferred\":123}");
            Directory.CreateDirectory(Path.Combine(app, "Rules", "_site")); File.WriteAllText(Path.Combine(app, "Rules", "_site", "TIS.ini"), "rules");
            Directory.CreateDirectory(Path.Combine(app, "logs")); File.WriteAllText(Path.Combine(app, "logs", "race.log"), "history");
            var source = new BackupService(app, user); var target = new BackupService(newApp, newUser);
            var archive = Path.Combine(root, "test.fluxbackup"); source.Export(archive, "backup-password");
            Check(!File.ReadAllText(archive).Contains(secret), "backup exposed credentials");
            var wrong = false; try { target.Read(archive, "wrong-password"); } catch (CryptographicException) { wrong = true; }
            Check(wrong && !target.HasPendingImport, "wrong password must not change configuration");
            var original = File.ReadAllText(archive); var envelope = JsonSerializer.Deserialize<BackupEnvelope>(original)!;
            envelope.Data[0] ^= 1; File.WriteAllText(archive, JsonSerializer.Serialize(envelope));
            var tampered = false; try { target.Read(archive, "backup-password"); } catch (CryptographicException) { tampered = true; }
            Check(tampered, "modified encrypted backup was accepted"); File.WriteAllText(archive, original);
            var package = target.Read(archive, "backup-password");
            File.WriteAllText(Path.Combine(newUser, "settings.json"), "{\"old\":true}");
            File.WriteAllText(Path.Combine(newApp, "FluxFTP-bookmarks.json"), "[]");
            target.StageImport(package);
            Check(File.ReadAllText(Path.Combine(newUser, "settings.json")).Contains("old"), "staging changed live configuration");
            Check(target.ApplyPending(), "staged backup did not apply");
            var ini = File.ReadAllText(Path.Combine(newApp, "FluxFTP-sites.ini"));
            var savedSecret = ini.Split("Password=")[1].Trim();
            Check(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(savedSecret[6..]), null, DataProtectionScope.CurrentUser)) == secret, "site password did not restore");
            using (var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(newUser, "settings.json"))))
            {
                var value = settings.RootElement.GetProperty("ApiPassword").GetString()!;
                Check(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value[6..]), null, DataProtectionScope.CurrentUser)) == secret, "JSON password escaping did not round trip");
                var irc = settings.RootElement.GetProperty("Irc");
                var nestedPassword = irc.GetProperty("Password").GetString()!;
                Check(irc.GetProperty("Enabled").GetBoolean() && Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    Convert.FromBase64String(nestedPassword[6..]), null, DataProtectionScope.CurrentUser)) == secret, "nested IRC settings did not round trip");
                var rules = settings.RootElement.GetProperty("Rules");
                Check(rules[0].GetProperty("Priority").GetInt32() == 2 && rules[1][1].GetString() == "*.jpg" &&
                    rules[2].ValueKind == JsonValueKind.Null, "nested objects/arrays/numbers/null changed during backup");
                Check(settings.RootElement.GetProperty("Layout").GetProperty("Left").GetProperty("Width").GetDouble() == 123.5,
                    "nested layout changed during backup");
            }
            using (var rules = JsonDocument.Parse(File.ReadAllText(Path.Combine(newApp, "FluxFTP-visionary-race.json"))))
            {
                var path = rules.RootElement.GetProperty("RushOptionsPath").GetString()!;
                Check(path.StartsWith(newApp) && File.ReadAllText(path).Contains("completeflag"), "external rule path not relocated");
            }
            Check(!File.Exists(Path.Combine(newApp, "FluxFTP-bookmarks.json")), "replace import left obsolete settings");
            Check(File.ReadAllText(Path.Combine(newApp, "Rules", "_site", "TIS.ini")) == "rules", "site rule lost");
            Check(File.ReadAllText(Path.Combine(newApp, "logs", "race.log")) == "history", "history lost");
            Check(!target.HasPendingImport && !target.ApplyPending(), "import applied twice");
            var recovery = Directory.GetFiles(Path.Combine(newUser, "backup-restore-history"), "*.fluxrestore").Single();
            target.StageImport(target.Read(recovery, "")); target.ApplyPending();
            Check(File.ReadAllText(Path.Combine(newUser, "settings.json")).Contains("old"), "recovery copy cannot restore original state");
            foreach (var path in new[] { "app/../outside.txt", "user/../settings.json", "app/Rules/CON.txt", "app/FluxFTP-sites.ini:stream", "app/Rules/a./b", "other/settings.json" })
            {
                var rejected = false;
                try { target.StageImport(new(1, DateTimeOffset.UtcNow, [new(path, [1])])); }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException) { rejected = true; }
                Check(rejected && !target.HasPendingImport, "unsafe backup path accepted: " + path);
            }
            var duplicate = false;
            try { target.StageImport(new(1, DateTimeOffset.UtcNow, [new("user/settings.json", []), new("user/SETTINGS.json", [])])); }
            catch (InvalidDataException) { duplicate = true; }
            Check(duplicate, "duplicate normalized path accepted");
            var prior = File.ReadAllBytes(Path.Combine(newUser, "settings.json"));
            target.StageImport(package);
            var failed = false; try { target.ApplyPending(index => { if (index == 1) throw new IOException("Injected write failure"); }); } catch (IOException) { failed = true; }
            Check(failed && prior.SequenceEqual(File.ReadAllBytes(Path.Combine(newUser, "settings.json"))) && !target.HasPendingImport, "failed import did not roll back");
            Console.WriteLine($"PASS: {count} backup checks: encryption, tampering, passwords, linked rules, restart staging, replacement, recovery, unsafe paths and rollback.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
