using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using IoFtp.Core.Models;
using IoFtp.Desktop.Services;
using Microsoft.Win32;

namespace IoFtp.Desktop;

public partial class SiteManagerWindow : Window
{
    private readonly ProfileStore _store = new();
    private readonly ObservableCollection<ConnectionProfile> _profiles;
    public ConnectionProfile? SelectedProfile { get; private set; }

    public SiteManagerWindow()
    {
        InitializeComponent();
        _profiles = new ObservableCollection<ConnectionProfile>(_store.Load());
        SitesList.ItemsSource = _profiles;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Profile is not null)
        {
            if (DescriptionConflict(dialog.Profile)) return;
            _profiles.Add(dialog.Profile); Save(); SitesList.SelectedItem = dialog.Profile;
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (SitesList.SelectedItem is not ConnectionProfile profile) return;
        var dialog = new ConnectionDialog(profile) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Profile is not null)
        {
            if (DescriptionConflict(dialog.Profile, profile.Id)) return;
            var index = _profiles.IndexOf(profile); _profiles[index] = dialog.Profile; Save(); SitesList.SelectedIndex = index;
        }
    }

    private bool DescriptionConflict(ConnectionProfile profile, Guid? exceptId = null)
    {
        var others = _profiles.Where(item => item.Id != exceptId).ToList();
        var conflict = !string.IsNullOrWhiteSpace(profile.Description) && others.Any(item =>
            item.Description.Equals(profile.Description, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Equals(profile.Description, StringComparison.OrdinalIgnoreCase)) ||
            others.Any(item => !string.IsNullOrWhiteSpace(item.Description) && item.Description.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
        if (!conflict) return false;
        MessageBox.Show("Site names and descriptions must be unique and cannot overlap.",
            "Site Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        return true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SitesList.SelectedItem is not ConnectionProfile profile) return;
        if (MessageBox.Show($"Delete '{profile.Name}'?", "Site Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        { _profiles.Remove(profile); Save(); }
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        if (SitesList.SelectedItem is not ConnectionProfile profile) return;
        var dialog = new SiteOptionsWindow(profile) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Options is not null)
        { var index = _profiles.IndexOf(profile); _profiles[index] = profile with { Options = dialog.Options, Proxy = dialog.SiteProxy }; Save(); SitesList.SelectedIndex = index; }
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (SitesList.SelectedItem is not ConnectionProfile profile) return;
        var dialog = new ConnectionDialog(profile, quickConnect: true) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Profile is not null)
        { SelectedProfile = dialog.Profile; DialogResult = true; }
    }

    private void ImportFtpRush_Click(object sender, RoutedEventArgs e)
        => ImportFtpRush();

    internal void ImportFtpRush()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var defaultFile = Path.Combine(documents, "FTPRush", "site.json");
        var legacyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FTPRush", "RushSite.xml");
        if (!File.Exists(defaultFile) && File.Exists(legacyFile)) defaultFile = legacyFile;
        var picker = new OpenFileDialog
        {
            Title = "Import FTPRush sites",
            Filter = "FTPRush sites (site.json;RushSite.xml)|site.json;RushSite.xml|JSON files (*.json)|*.json|Legacy FTPRush XML (*.xml)|*.xml",
            InitialDirectory = File.Exists(defaultFile) ? Path.GetDirectoryName(defaultFile) : documents,
            FileName = File.Exists(defaultFile) ? Path.GetFileName(defaultFile) : "site.json"
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var imported = FtpRushSiteImporter.ImportPackage(picker.FileName);
            var dialog = new FtpRushImportWindow("FTPRush", imported.Sites, _profiles, imported.Bookmarks) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var importedSites = 0;
            var plannedProfiles = _profiles.ToList();
            var siteNames = _profiles.ToDictionary(profile => profile.Name, profile => profile.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var profile in dialog.SelectedProfiles)
            {
                var index = plannedProfiles.Select((existing, position) => (existing, position))
                    .Where(item => IsSameSite(item.existing, profile))
                    .Select(item => item.position).DefaultIfEmpty(-1).First();
                if (index >= 0)
                {
                    siteNames[profile.Name] = plannedProfiles[index].Name;
                    if (!dialog.ReplaceExisting) continue;
                    plannedProfiles[index] = profile with { Id = plannedProfiles[index].Id, Name = plannedProfiles[index].Name,
                        Password = string.IsNullOrEmpty(profile.Password) ? plannedProfiles[index].Password : profile.Password };
                }
                else { plannedProfiles.Add(profile); siteNames[profile.Name] = profile.Name; }
                importedSites++;
            }
            var bookmarkStore = new BookmarkStore();
            var bookmarks = bookmarkStore.Load();
            var importedBookmarks = 0;
            foreach (var sourceBookmark in dialog.SelectedBookmarks)
            {
                var bookmark = sourceBookmark with { SiteName = siteNames.GetValueOrDefault(sourceBookmark.SiteName, sourceBookmark.SiteName) };
                var index = bookmarks.FindIndex(existing =>
                    existing.SiteName.Equals(bookmark.SiteName, StringComparison.OrdinalIgnoreCase) &&
                    existing.Name.Equals(bookmark.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    if (!dialog.ReplaceExisting) continue;
                    bookmarks[index] = bookmark;
                }
                else bookmarks.Add(bookmark);
                importedBookmarks++;
            }
            var sectionStore = new SectionStore();
            var sections = FtpRushSectionMigration.Merge(sectionStore.Load(), dialog.SelectedSections, siteNames, dialog.ReplaceExisting);
            foreach (var path in dialog.SelectedVisionaryFiles) _ = new VisionaryConfiguration(path);
            if (dialog.SelectedVisionaryFiles.Count > 0) _ = VisionaryImportInventory.Load();
            var backup = ImportBackup.Commit(AppContext.BaseDirectory,
                ["FluxFTP-sites.ini", "FluxFTP-bookmarks.json", "FluxFTP-sections.json", "FluxFTP-visionary-files.json"], () =>
                {
                    _store.Save(plannedProfiles);
                    bookmarkStore.Save(bookmarks);
                    if (dialog.SelectedSections.Count > 0) sectionStore.Save(sections);
                    if (dialog.SelectedVisionaryFiles.Count > 0) VisionaryImportInventory.Link(dialog.SelectedVisionaryFiles);
                });
            _profiles.Clear(); foreach (var profile in plannedProfiles) _profiles.Add(profile);
            MessageBox.Show($"Imported {importedSites} FTPRush site(s), {importedBookmarks} bookmark(s).\n" +
                $"Processed {dialog.SelectedSections.Count} section path(s) with {(dialog.ReplaceExisting ? "replace" : "skip existing")} policy; linked {dialog.SelectedVisionaryFiles.Count} VISIONARY rule files.\n" +
                $"Existing rules and section prechecks are preserved. Passwords stored by FluxFTP use Windows DPAPI.\nBackup: {backup}",
                "FTPRush Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"FTPRush import failed: {exception.Message}", "FTPRush Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportFlashFxp_Click(object sender, RoutedEventArgs e)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var defaultFile = Path.Combine(desktop, "FlashFXP Sites.ftp");
        var picker = new OpenFileDialog
        {
            Title = "Import FlashFXP sites",
            Filter = "FlashFXP site exports (*.ftp)|*.ftp|XML files (*.xml)|*.xml",
            InitialDirectory = desktop,
            FileName = File.Exists(defaultFile) ? Path.GetFileName(defaultFile) : ""
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var imported = FlashFxpSiteImporter.Import(picker.FileName);
            var dialog = new FtpRushImportWindow("FlashFXP", imported, _profiles) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            foreach (var profile in dialog.SelectedProfiles) _profiles.Add(profile);
            Save();
            MessageBox.Show($"Imported {dialog.SelectedProfiles.Count} FlashFXP site(s). Passwords are now protected with Windows DPAPI.",
                "FlashFXP Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"FlashFXP import failed: {exception.Message}", "FlashFXP Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save() => _store.Save(_profiles);

    private static bool IsSameSite(ConnectionProfile left, ConnectionProfile right) =>
        left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase) ||
        left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port &&
        left.Username.Equals(right.Username, StringComparison.OrdinalIgnoreCase);
}
