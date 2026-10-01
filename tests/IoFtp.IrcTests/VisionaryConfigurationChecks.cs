using System.Text;
using IoFtp.Desktop.Services;

internal static class VisionaryConfigurationChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-visionary-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        void Check(bool condition, string name) { if (!condition) throw new Exception("VISIONARY: " + name); }
        try
        {
            var path = Path.Combine(directory, "rushopt.ini");
            const string original = "; keep comments\r\n[normal]\r\ncompleteflag=COMPLETE\r\nrefresh=1000\r\nrefresh=500\r\n";
            File.WriteAllText(path, original, new UTF8Encoding(true));
            var bytes = File.ReadAllBytes(path);
            var file = new VisionaryConfiguration(path);
            Check(file.Entries.Count == 3 && !file.HasChanges, "order and duplicate keys retained");
            file.Save();
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "unchanged import is byte exact");
            file.Entries[0].Value = "FINISHED";
            Check(file.HasChanges, "edits tracked");
            file.Save();
            Check(File.ReadAllText(path) == original.Replace("COMPLETE", "FINISHED"), "only value changed");
            Check(File.ReadAllBytes(Directory.GetFiles(directory, "*.bak").Single()).SequenceEqual(bytes), "backup is original bytes");
            Check(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }), "BOM retained");
            File.AppendAllText(path, "; concurrent edit");
            var blocked = false;
            try { file.Save(); } catch (IOException) { blocked = true; }
            Check(blocked, "external changes protected");
            var cha = Path.Combine(directory, "section.cha");
            File.WriteAllText(cha, "[GLOBAL]\n(?i)(?=TV).* =TV\n");
            var rules = new VisionaryConfiguration(cha);
            Check(rules.Entries.Single().Key == "(?i)(?=TV).*", "regex equals preserved");
            rules.Entries[0].Value = "TV-HD"; rules.Save();
            Check(File.ReadAllText(cha) == "[GLOBAL]\n(?i)(?=TV).* =TV-HD\n", "regex key not damaged");
            rules.Entries[0].Value = "bad\n[INJECTED]";
            blocked = false;
            try { rules.Save(); } catch (InvalidDataException) { blocked = true; }
            Check(blocked, "multiline values rejected");
            var slftp = Path.Combine(directory, "sections.txt");
            File.WriteAllText(slftp, "[mappings]\n;TV;/S01/i\nTV;TV-HD;/1080p/i\n// comment\n");
            var mappings = new VisionaryConfiguration(slftp);
            Check(mappings.Entries.Count == 2 && !mappings.HasChanges, "SLFTP global and section mappings imported");
            mappings.Entries[0].Value = "/S02/i"; mappings.Save();
            Check(File.ReadAllText(slftp).Contains(";TV;/S02/i"), "SLFTP mapping delimiters preserved");
            Console.WriteLine("PASS: VISIONARY configuration preservation, backups and conflict checks.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
