using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IoFtp.Desktop.Services;

internal sealed record BackupFile(string Path, byte[] Data);
internal sealed record BackupPackage(int Version, DateTimeOffset Created, List<BackupFile> Files);
internal sealed record BackupEnvelope(string Format, byte[] Salt, byte[] Nonce, byte[] Tag, byte[] Data);
internal sealed record PendingBackup(string AppDirectory, BackupPackage Package);

internal sealed class BackupService(string appDirectory, string userDirectory)
{
    private const int MaximumBytes = 512 * 1024 * 1024;
    private readonly string _app = Path.GetFullPath(appDirectory);
    private readonly string _user = Path.GetFullPath(userDirectory);
    private string PendingPath => Path.Combine(_user, "pending-backup-import.dpapi");
    public bool HasPendingImport => File.Exists(PendingPath);
    public static BackupService Current() => new(AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFTP"));
    private static readonly Regex SecretToken = new(@"dpapi:[A-Za-z0-9+/=]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PortableToken = new(@"flux-secret:[A-Za-z0-9+/=]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static bool TextFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ini" or ".json" or ".txt" or ".xml" or ".cha" or ".nfo";
    private static bool Managed(string scope, string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is ".fluxbackup" or ".fluxrestore" or ".dpapi") return false;
        if (path.Contains('\\') || path.Contains(':') || path.StartsWith('/') ||
            path.Split('/').Any(part => part is "" or "." or ".." || part.Any(character => char.IsControl(character) || Path.GetInvalidFileNameChars().Contains(character)) || part.EndsWith('.') || part.EndsWith(' ') ||
                Regex.IsMatch(part, @"^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))) return false;
        if (scope == "user") return !path.Contains('/') && !path.StartsWith("pending-", StringComparison.OrdinalIgnoreCase) &&
            (TextFile(path) || Path.GetExtension(path).Equals(".pfx", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".log", StringComparison.OrdinalIgnoreCase));
        if (scope != "app") return false;
        return path.StartsWith("Rules/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("logs/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("backup-imported/", StringComparison.OrdinalIgnoreCase) ||
            !path.Contains('/') && path.StartsWith("FluxFTP-", StringComparison.OrdinalIgnoreCase) &&
            (TextFile(path) || Path.GetExtension(path).Equals(".log", StringComparison.OrdinalIgnoreCase));
    }
    private string Target(string name)
    {
        var slash = name.IndexOf('/');
        if (slash < 0 || !Managed(name[..slash], name[(slash + 1)..])) throw new InvalidDataException("Invalid backup file path: " + name);
        var root = name[..slash] == "app" ? _app : _user;
        var target = Path.GetFullPath(Path.Combine(root, name[(slash + 1)..].Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Backup path escaped its configuration directory.");
        for (var current = target; current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Backup paths cannot pass through a junction or symbolic link.");
        return target;
    }
    private List<BackupFile> Capture()
    {
        var files = new List<BackupFile>();
        foreach (var (scope, root) in new[] { ("app", _app), ("user", _user) })
        {
            if (!Directory.Exists(root)) continue;
            void Scan(string directory)
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Cannot back up a linked configuration directory.");
                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    var name = Path.GetRelativePath(root, path).Replace('\\', '/');
                    if (!Managed(scope, name)) continue;
                    _ = Target(scope + "/" + name);
                    if (new FileInfo(path).Length > MaximumBytes) throw new IOException("Backup file exceeds 512 MB: " + name);
                    files.Add(new(scope + "/" + name, File.ReadAllBytes(path)));
                }
                if (scope == "app")
                    foreach (var child in Directory.EnumerateDirectories(directory))
                        if (Managed(scope, Path.GetRelativePath(root, child).Replace('\\', '/') + "/placeholder")) Scan(child);
            }
            Scan(root);
        }
        Validate(new(1, DateTimeOffset.UtcNow, files));
        return files;
    }
    private void Validate(BackupPackage package)
    {
        if (package.Version != 1 || package.Files is null || package.Files.Count > 10000) throw new InvalidDataException("Unsupported or invalid FluxFTP backup.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var file in package.Files)
        {
            if (file is null || file.Data is null || !names.Add(file.Path)) throw new InvalidDataException("Duplicate or invalid backup entry.");
            _ = Target(file.Path);
            bytes += file.Data.LongLength;
            if (bytes > MaximumBytes) throw new InvalidDataException("Backup exceeds 512 MB of configuration and history.");
        }
    }
    private static void AtomicWrite(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, data); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string RewritePaths(string text, Dictionary<string, string> replacements)
    {
        foreach (var (from, to) in replacements.OrderByDescending(pair => pair.Key.Length))
        {
            text = text.Replace(JsonSerializer.Serialize(from)[1..^1], JsonSerializer.Serialize(to)[1..^1], StringComparison.OrdinalIgnoreCase);
            text = text.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }
    private static byte[] RewriteText(byte[] bytes, bool json, Func<string, string> replace)
    {
        if (!json) return Encoding.UTF8.GetBytes(replace(Encoding.UTF8.GetString(bytes)));
        JsonNode? Visit(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text)) return JsonValue.Create(replace(text));
            if (node is JsonObject obj)
            {
                var copy = new JsonObject();
                foreach (var (key, child) in obj) copy[key] = Visit(child);
                return copy;
            }
            if (node is JsonArray array)
            {
                var copy = new JsonArray();
                foreach (var item in array) copy.Add(Visit(item));
                return copy;
            }
            return node?.DeepClone();
        }
        return JsonSerializer.SerializeToUtf8Bytes(Visit(JsonNode.Parse(bytes)));
    }
    public int Export(string destination, string password)
    {
        if (!Path.GetExtension(destination).Equals(".fluxbackup", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a .fluxbackup file.");
        if (password.Length < 8) throw new ArgumentException("Use a backup password of at least 8 characters.");
        var files = Capture();
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Preserve externally linked VISIONARY rules and portable script files without writing into their old locations on import.
        foreach (var file in files.Where(file => Path.GetExtension(file.Path) == ".json").ToArray())
        {
            using var document = JsonDocument.Parse(file.Data);
            void Inspect(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) Inspect(property.Value);
                else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Inspect(item);
                else if (element.ValueKind == JsonValueKind.String && element.GetString() is { } path && Path.IsPathFullyQualified(path) && File.Exists(path) &&
                    (TextFile(path) || Path.GetExtension(path).ToLowerInvariant() is ".ps1" or ".py" or ".tcl" or ".mrc") && !replacements.ContainsKey(path))
                {
                    if (new FileInfo(path).Length > MaximumBytes) throw new IOException("Linked rule/script file exceeds 512 MB.");
                    var imported = "backup-imported/" + replacements.Count.ToString("D4") + "-" + Path.GetFileName(path);
                    replacements[path] = "@FLUXFTP_APP@/" + imported;
                    files.Add(new("app/" + imported, File.ReadAllBytes(path)));
                }
            }
            Inspect(document.RootElement);
        }
        for (var i = 0; i < files.Count; i++)
            if (TextFile(files[i].Path))
            {
                files[i] = files[i] with { Data = RewriteText(files[i].Data, Path.GetExtension(files[i].Path) == ".json", text =>
                    SecretToken.Replace(RewritePaths(text, replacements), match => "flux-secret:" + Convert.ToBase64String(
                        ProtectedData.Unprotect(Convert.FromBase64String(match.Value[6..]), null, DataProtectionScope.CurrentUser)))) };
            }
        var package = new BackupPackage(1, DateTimeOffset.UtcNow, files); Validate(package);
        var plain = JsonSerializer.SerializeToUtf8Bytes(package);
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var encrypted = new byte[plain.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, encrypted, tag);
            AtomicWrite(destination, JsonSerializer.SerializeToUtf8Bytes(new BackupEnvelope("FluxFTP-backup-v1", salt, nonce, tag, encrypted)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain);
            foreach (var file in files) CryptographicOperations.ZeroMemory(file.Data);
        }
        return files.Count;
    }
    public BackupPackage Read(string source, string password)
    {
        if (new FileInfo(source).Length > MaximumBytes * 2L) throw new InvalidDataException("Backup file is too large.");
        var data = File.ReadAllBytes(source);
        if (Path.GetExtension(source).Equals(".fluxrestore", StringComparison.OrdinalIgnoreCase))
            data = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        else
        {
            var envelope = JsonSerializer.Deserialize<BackupEnvelope>(data) ?? throw new InvalidDataException("Invalid backup.");
            if (envelope.Format != "FluxFTP-backup-v1" || envelope.Salt.Length != 16 || envelope.Nonce.Length != 12 || envelope.Tag.Length != 16)
                throw new InvalidDataException("Unsupported backup format.");
            var key = Rfc2898DeriveBytes.Pbkdf2(password, envelope.Salt, 210000, HashAlgorithmName.SHA256, 32);
            data = new byte[envelope.Data.Length];
            try { using var aes = new AesGcm(key, 16); aes.Decrypt(envelope.Nonce, envelope.Data, envelope.Tag, data); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        try
        {
            var package = JsonSerializer.Deserialize<BackupPackage>(data) ?? throw new InvalidDataException("Invalid backup contents.");
            Validate(package);
            return package;
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
    public void StageImport(BackupPackage package)
    {
        Validate(package);
        var restored = new List<BackupFile>();
        foreach (var file in package.Files)
        {
            var bytes = file.Data;
            if (TextFile(file.Path))
            {
                bytes = RewriteText(bytes, Path.GetExtension(file.Path) == ".json", text =>
                    PortableToken.Replace(text.Replace("@FLUXFTP_APP@", _app.TrimEnd('\\', '/'), StringComparison.Ordinal), match =>
                        "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Convert.FromBase64String(match.Value[12..]), null, DataProtectionScope.CurrentUser))));
            }
            restored.Add(new(file.Path, bytes));
        }
        var pending = JsonSerializer.SerializeToUtf8Bytes(new PendingBackup(_app, new(1, package.Created, restored)));
        try { AtomicWrite(PendingPath, ProtectedData.Protect(pending, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(pending); }
    }
    public void CancelPendingImport() { if (File.Exists(PendingPath)) File.Delete(PendingPath); }
    public bool ApplyPending(Action<int>? beforeWrite = null)
    {
        if (!HasPendingImport) return false;
        var pending = JsonSerializer.Deserialize<PendingBackup>(ProtectedData.Unprotect(File.ReadAllBytes(PendingPath), null, DataProtectionScope.CurrentUser))
            ?? throw new InvalidDataException("Invalid pending import.");
        if (!Path.GetFullPath(pending.AppDirectory).Equals(_app, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This import was prepared for another FluxFTP installation.");
        Validate(pending.Package);
        var old = new BackupPackage(1, DateTimeOffset.UtcNow, Capture());
        var targets = old.Files.Select(file => file.Path).Concat(pending.Package.Files.Select(file => file.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var name in targets) if (Directory.Exists(Target(name))) throw new IOException("A directory occupies an imported file path: " + name);
        var recovery = Path.Combine(_user, "backup-restore-history", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".fluxrestore");
        AtomicWrite(recovery, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(old), null, DataProtectionScope.CurrentUser));
        var replacements = pending.Package.Files.ToDictionary(file => file.Path, file => file.Data, StringComparer.OrdinalIgnoreCase);
        var prior = old.Files.ToDictionary(file => file.Path, file => file.Data, StringComparer.OrdinalIgnoreCase);
        try
        {
            var index = 0;
            foreach (var name in targets)
            {
                beforeWrite?.Invoke(index++);
                var target = Target(name);
                if (replacements.TryGetValue(name, out var bytes)) AtomicWrite(target, bytes);
                else if (File.Exists(target)) File.Delete(target);
            }
            CancelPendingImport();
            return true;
        }
        catch
        {
            foreach (var name in targets)
            {
                var target = Target(name);
                if (prior.TryGetValue(name, out var bytes)) AtomicWrite(target, bytes);
                else if (File.Exists(target)) File.Delete(target);
            }
            CancelPendingImport();
            throw;
        }
    }
}
