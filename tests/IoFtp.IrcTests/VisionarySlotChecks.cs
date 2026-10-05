using IoFtp.Core.Models;
using IoFtp.Desktop.Services;

internal static class VisionarySlotChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-slot-check-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "TEST.ini");
        var profile = new ConnectionProfile(Guid.NewGuid(), "TEST", "example.invalid", 21, "user", TransferProtocol.Ftp,
            Options: new SiteOptions(MaxSlots: 4, MaxUploadSlots: 2, MaxDownloadSlots: 2));
        void Check(string text, int up, int down)
        {
            File.WriteAllText(path, text);
            var options = VisionarySiteMigration.Apply(profile, [path]).EffectiveOptions;
            if (options.MaxSlots != 4 || options.MaxUploadSlots != up || options.MaxDownloadSlots != down)
                throw new Exception("VISIONARY slot import failed");
        }
        try
        {
            Check("[MP3]\nslotsup=3\nslotsdn=3\n[TV]\nslotsup=3\nslotsdn=3", 3, 3);
            Check("[MP3]\nslotsup=8\nslotsdn=0", 4, 0);
            Check("[MP3]\nslotsup=3\nslotsdn=3\n[TV]\nslotsup=1\nslotsdn=3", 2, 3);
            Check("[MP3]\nslotsup=-1\nslotsdn=invalid", 2, 2);
            Check("[MP3]\nsectslotsup=1\nsectslotsdn=1", 2, 2);
            Console.WriteLine("PASS: Visionary direction slots imported within unchanged total; ambiguous/missing values retained.");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
