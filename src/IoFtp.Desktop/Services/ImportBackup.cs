using System.IO;

namespace IoFtp.Desktop.Services;

internal static class ImportBackup
{
    public static string Commit(string directory, IReadOnlyList<string> names, Action save)
    {
        if (names.Any(name => System.IO.Path.GetFileName(name) != name)) throw new ArgumentException("Expected configuration filenames.");
        var before = names.ToDictionary(name => name, name => File.Exists(System.IO.Path.Combine(directory, name))
            ? File.ReadAllBytes(System.IO.Path.Combine(directory, name)) : null);
        var backup = System.IO.Path.Combine(directory, "Import-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backup);
        foreach (var (name, bytes) in before)
            if (bytes is not null) File.WriteAllBytes(System.IO.Path.Combine(backup, name), bytes);
        try { save(); }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var (name, bytes) in before)
            {
                try
                {
                    var target = System.IO.Path.Combine(directory, name);
                    if (bytes is null) File.Delete(target); else File.WriteAllBytes(target, bytes);
                }
                catch (Exception ex) { errors.Add(ex); }
            }
            if (errors.Count > 1) throw new AggregateException($"Import failed and rollback needs attention. Backup: {backup}", errors);
            throw new IOException($"Import failed; previous configuration restored. Backup: {backup}. {failure.Message}", failure);
        }
        return backup;
    }
}
