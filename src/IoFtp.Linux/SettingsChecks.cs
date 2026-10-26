using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using IoFtp.Core.Models;
namespace IoFtp.Linux;
internal static class SettingsChecks
{
    public static int Run()
    {
        var previous = Environment.GetEnvironmentVariable("FLUXFTP_CONFIG_HOME");
        var temporary = Path.Combine(Path.GetTempPath(), "fluxftp-settings-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("FLUXFTP_CONFIG_HOME", temporary);
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { count++; return; } throw new Exception(name); }
        try
        {
            var configuration = new LinuxConfiguration(); var options = configuration.Load().Options;
            var api = new LinuxApiOptions(Port: 55478, Password: "keep-this-password");
            configuration.Update(state => state with { Options = state.Options with { Api = api, Layout = new(Metrics: false) } });
            var edited = options with { DownloadPath = Path.Combine(temporary, "downloads"), MaxLocalDownloads = 3, MaxLocalUploads = 2, SerializeLocalTransfers = false,
                StarterTimeoutSeconds = 27, MaxTransferRuntimeMinutes = 0, LogBufferLines = 1234, ClearCompletedManualTransfers = true,
                Proxy = new(ProxyType.Socks5, "proxy.invalid", 1080, "proxy-user", "proxy-secret") };
            LinuxSettings.SaveOptions(configuration, edited);
            var saved = configuration.Load().Options;
            Check(saved.Api == api && saved.Layout?.Metrics == false, "Global settings overwrote independently saved API/layout options");
            Check(saved.MaxLocalDownloads == 3 && saved.MaxLocalUploads == 2 && !saved.SerializeLocalTransfers, "Local slot settings lost");
            Check(saved.StarterTimeoutSeconds == 27 && saved.MaxTransferRuntimeMinutes == 0, "Timeout/unlimited runtime lost");
            Check(saved.LogBufferLines == 1234 && saved.ClearCompletedManualTransfers && Directory.Exists(saved.DownloadPath), "Log/cleanup/folder settings lost");
            Check(saved.Proxy?.Password == "proxy-secret", "Encrypted proxy credentials lost");
            Check(!File.ReadAllText(Path.Combine(temporary, "state.enc")).Contains("proxy-secret"), "Secret stored in plaintext");
            foreach (var invalid in new[] { edited with { DownloadPath = "relative" }, edited with { MaxLocalDownloads = -1 }, edited with { MaxLocalUploads = 2049 },
                edited with { StarterTimeoutSeconds = 0 }, edited with { MaxTransferRuntimeMinutes = -1 }, edited with { LogBufferLines = 1 }, edited with { Proxy = new(ProxyType.Socks5, "", 0) } })
                Reject(() => LinuxSettings.SaveOptions(configuration, invalid), "Invalid global options accepted");
            Check(configuration.Load().Options == saved, "Invalid global settings partially saved");
            var profile = new ConnectionProfile(Guid.NewGuid(), "SOURCE", "ftp.invalid", 21, "!exact-user", TransferProtocol.FtpsExplicit, Password: "site-secret",
                Options: new SiteOptions(MaxSlots: 7, MaxUploadSlots: 3, MaxDownloadSlots: 4, Priority: 80, ImportedSkipRules: "keep-original-rules", UseOpenSslTls: true));
            LinuxSettings.SaveSite(configuration, profile);
            LinuxSettings.SaveSection(configuration, null, "TV", new() { ["SOURCE"] = "/tv" });
            LinuxSettings.SaveSite(configuration, profile with { Name = "RENAMED" });
            var site = configuration.Load().Sites.Single().Restore();
            Check(site.Username == "!exact-user" && site.Password == "site-secret", "Site save changed login credentials");
            Check(site.EffectiveOptions.ImportedSkipRules == "keep-original-rules" && site.EffectiveOptions.UseOpenSslTls, "Unedited site options lost");
            Check(configuration.Load().Sections.Single().SitePaths["RENAMED"] == "/tv", "Renamed site lost section mapping");
            Reject(() => LinuxSettings.SaveSite(configuration, profile with { Id = Guid.NewGuid(), Name = "renamed" }), "Duplicate site accepted");
            Reject(() => LinuxSettings.SaveSite(configuration, profile with { Options = profile.EffectiveOptions with { MaxSlots = 1 } }), "Invalid slot budget accepted");
            Reject(() => LinuxSettings.SaveSection(configuration, "TV", "TV", new() { ["RENAMED"] = "/changed", ["MISSING"] = "/bad" }), "Unknown section site accepted");
            Check(configuration.Load().Sections.Single().SitePaths["RENAMED"] == "/tv", "Failed section save partially changed state");
            Reject(() => LinuxSettings.SaveSection(configuration, "TV", "TV", new() { ["RENAMED"] = "/../escape" }), "Unsafe section path accepted");
            configuration.Update(state => state with { Options = state.Options with { Layout = new(), Proxy = null, ClearCompletedManualTransfers = false } });
            var window = new MainWindow(); window.Show(); Dispatcher.UIThread.RunJobs();
            var toolbar = window.GetLogicalDescendants().OfType<WrapPanel>().Single(control => control.Name == "MainToolbar");
            var labels = toolbar.Children.OfType<Button>().Select(button => button.Content?.ToString()).ToArray();
            Check(labels.SequenceEqual(new[] { "Sites", "Tools", "Metrics", "Manager", "Queue", "Log", "Global Settings", "About" }), "Main toolbar differs from Windows order");
            foreach (var panel in new[] { "Metrics", "Manager", "Queue", "Log" })
            {
                var button = toolbar.Children.OfType<Button>().Single(button => Equals(button.Content, panel));
                var control = window.GetLogicalDescendants().OfType<Control>().Single(control => control.Name == panel + "Panel");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!control.IsVisible, panel + " did not hide");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(control.IsVisible, panel + " did not restore");
            }
            Check(configuration.Load().Options.Api == api, "Panel toggles overwrote API options");
            window.Width = 950; window.Height = 700; window.UpdateLayout();
            Check(toolbar.Children.OfType<Button>().All(button => button.Bounds.Height is > 0 and < 50), "Toolbar buttons stretch when wrapping");
            var dialog = new GlobalSettingsWindow(configuration.Load().Options, value => { LinuxSettings.SaveOptions(configuration, value); return Task.CompletedTask; }, _ => Task.FromResult(false), _ => Task.CompletedTask);
            dialog.Show(window); Dispatcher.UIThread.RunJobs();
            var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
            Check(tabs.Items.OfType<TabItem>().Select(item => item.Header?.ToString()).SequenceEqual(new[] { "Local", "Jobs & log", "Proxy", "Backup/Import", "API" }), "Settings tabs missing");
            var downloadSlots = dialog.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(field => field.Name == "DownloadSlots");
            var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "SaveSettings");
            downloadSlots.Text = "-1"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(dialog.IsVisible && !string.IsNullOrEmpty(dialog.GetLogicalDescendants().OfType<TextBlock>().Single(field => field.Name == "SettingsError").Text), "Invalid settings closed dialog without error");
            downloadSlots.Text = "5"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!dialog.IsVisible && configuration.Load().Options.MaxLocalDownloads == 5 && configuration.Load().Options.Api == api, "Settings Save button failed to apply values");
            var sitesWindow = new SitesWindow(configuration, () => { }, (_, _) => Task.CompletedTask); sitesWindow.Show(window); Dispatcher.UIThread.RunJobs();
            Check(sitesWindow.GetLogicalDescendants().OfType<TextBox>().Any(field => field.Text == "!exact-user"), "Sites editor did not load saved site"); sitesWindow.Close();
            var sectionsWindow = new SectionsWindow(configuration, () => { }); sectionsWindow.Show(window); Dispatcher.UIThread.RunJobs();
            Check(sectionsWindow.GetLogicalDescendants().OfType<ListBox>().Single().ItemCount == 1, "Sections editor did not load sections"); sectionsWindow.Close();
            LinuxSettings.DeleteSite(configuration, site.Id);
            Check(configuration.Load().Sites.Count == 0 && configuration.Load().Sections.Single().SitePaths.Count == 0, "Site delete left section mappings");
            window.Close(); Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"PASS: {count} Linux settings/UI checks: encrypted persistence, validation, API preservation, site rename/delete, sections, toolbar, panel toggles, responsive sizing and settings dialog Save.");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLUXFTP_CONFIG_HOME", previous);
            // Only this run's private temporary configuration folder is removed.
            if (Directory.Exists(temporary) && Path.GetFullPath(temporary).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal) && Path.GetFileName(temporary).StartsWith("fluxftp-settings-", StringComparison.Ordinal)) Directory.Delete(temporary, true);
        }
    }
}
