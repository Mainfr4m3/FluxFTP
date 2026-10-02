using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using IoFtp.Core.Models;
using IoFtp.Desktop.Services;
using Microsoft.Win32;

namespace IoFtp.Desktop;

public partial class FtpRushImportWindow : Window
{
    private readonly List<ImportRow> _rows;
    private readonly IReadOnlyList<SiteBookmark> _bookmarks;
    private readonly IReadOnlyCollection<ConnectionProfile> _existing;
    private readonly List<FtpRushSectionRow> _sections;
    private IReadOnlyList<VisionaryImportFile> _visionary = [];
    private IReadOnlyDictionary<string, string> _passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ConnectionProfile> SelectedProfiles { get; private set; } = [];
    internal HashSet<string> PasswordUpdates { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyList<SiteBookmark> SelectedBookmarks { get; private set; } = [];
    internal bool ReplaceExisting { get; private set; }
    internal IReadOnlyList<FtpRushSectionRow> SelectedSections { get; private set; } = [];
    internal IReadOnlyList<string> SelectedVisionaryFiles { get; private set; } = [];

    internal FtpRushImportWindow(string sourceName, IReadOnlyList<FtpRushImportedSite> sites,
        IReadOnlyCollection<ConnectionProfile> existing, IReadOnlyList<SiteBookmark>? bookmarks = null)
    {
        InitializeComponent();
        _existing = existing;
        Title = $"Import {sourceName} Sites";
        HeadingText.Text = $"IMPORT {sourceName.ToUpperInvariant()} SITES";
        _rows = sites.Select(site => new ImportRow(site,
            existing.Any(item => item.Name.Equals(site.Profile.Name, StringComparison.OrdinalIgnoreCase) ||
                (item.Host.Equals(site.Profile.Host, StringComparison.OrdinalIgnoreCase) && item.Port == site.Profile.Port && item.Username.Equals(site.Profile.Username, StringComparison.OrdinalIgnoreCase)))))
            .ToList();
        _bookmarks = bookmarks ?? [];
        _sections = FtpRushSectionMigration.Preview(_bookmarks);
        var ambiguousSites = sites.GroupBy(site => site.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var section in _sections.Where(row => ambiguousSites.Contains(row.Site)))
        { section.Selected = false; section.Note = "Several Rush sites share this name. Resolve the site identity before selecting this mapping."; }
        SectionsGrid.ItemsSource = _sections;
        if (bookmarks is null)
        {
            GuidePanel.Visibility = Visibility.Collapsed;
            ImportBookmarksBox.IsChecked = false;
            ImportSectionsBox.IsChecked = false;
            SectionsTab.Visibility = Visibility.Collapsed;
            VisionaryTab.Visibility = Visibility.Collapsed;
        }
        else
        {
            foreach (var row in _rows) row.Selected = true;
            ReplaceBox.IsChecked = true;
        }
        SitesGrid.ItemsSource = _rows;
        SummaryText.Text = bookmarks is null
            ? $"{_rows.Count} compatible {sourceName} site(s) found. Existing duplicates are unselected."
            : $"{_rows.Count} site(s), {_bookmarks.Count} bookmark(s), {_sections.Count(row => row.Selected)} ready section paths. Review Sections; add your VISIONARY folder under Rules. Legacy XML passwords may need re-entry.";
        if (bookmarks is not null && ambiguousSites.Count > 0)
            SummaryText.Text += " Section paths for duplicate site names are unselected. Give each site a unique name in Rush and export again.";
        if (sourceName == "FTPRush" && sites.Any(site => string.IsNullOrEmpty(site.Profile.Password)))
            Loaded += (_, _) =>
            {
                if (ImportPasswordPrompt.Show(this))
                    PasswordFile_Click(this, new RoutedEventArgs());
            };
    }

    private void PasswordFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Site passwords: FTP URLs or Site name=password", Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var passwords = FtpRushPasswordFile.Read(picker.FileName, _rows.Select(row => row.Site.Profile));
            _passwords = passwords;
            foreach (var row in _rows) row.PasswordFromFile = passwords.ContainsKey(row.Name);
            PasswordFileStatus.Text = $"{passwords.Count} site password(s) matched. The checkbox controls password updates on existing sites, including Skip existing. Passwords are protected when saved.";
        }
        catch (Exception ex)
        {
            // Never include decoder data or file contents in a UI error.
            PasswordFileStatus.Text = ex is System.IO.InvalidDataException ? ex.Message : "Cannot read password file. Use a readable UTF-8 text file.";
        }
    }
    private void ClearPasswordFile_Click(object sender, RoutedEventArgs e)
    {
        _passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows) row.PasswordFromFile = false;
        PasswordFileStatus.Text = "Password file cleared. Original imported passwords will be used; empty values do not replace saved passwords.";
    }

    private void VisionaryFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Select VISIONARY or User_Files folder" };
        if (picker.ShowDialog(this) != true) return;
        _visionary = [];
        VisionaryGrid.ItemsSource = _visionary;
        try
        {
            _visionary = VisionaryImportInventory.ReadFolder(picker.FolderName);
            VisionaryGrid.ItemsSource = _visionary;
            RulesStatus.Text = $"{_visionary.Count} files / {_visionary.Sum(file => file.Settings)} settings. Imported as links to the originals. Section paths come from Rush bookmarks; section.cha contains classification rules, not FTP paths. Existing VISIONARY database rules keep running in VISIONARY.";
        }
        catch (Exception ex) { RulesStatus.Text = $"Cannot import this folder: {ex.Message}"; }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        SitesGrid.CommitEdit();
        SectionsGrid.CommitEdit(); SectionsGrid.CommitEdit();
        SelectedProfiles = ImportSitesBox.IsChecked == true
            ? _rows.Where(row => row.Selected).Select(row => _passwords.TryGetValue(row.Name, out var password)
                ? row.Site.Profile with { Password = password } : row.Site.Profile).ToList() : [];
        try { SelectedProfiles = SelectedProfiles.Select(profile => VisionarySiteMigration.Apply(profile, _visionary.Select(file => file.Path))).ToList(); }
        catch (Exception ex) { SummaryText.Text = $"Cannot import VISIONARY site options: {ex.Message}"; return; }
        PasswordUpdates = UpdatePasswordsBox.IsChecked == true
            ? SelectedProfiles.Where(profile => _passwords.ContainsKey(profile.Name)).Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase);
        if (SelectedProfiles.GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        { SummaryText.Text = "Several selected Rush sites have the same name. Give them unique names in Rush, or select one site at a time; ambiguous paths are not imported automatically."; return; }
        SelectedBookmarks = ImportBookmarksBox.IsChecked == true ? _bookmarks : [];
        ReplaceExisting = ReplaceBox.IsChecked == true;
        SelectedSections = ImportSectionsBox.IsChecked == true ? _sections.Where(row => row.Selected).ToList() : [];
        SelectedVisionaryFiles = _visionary.Select(file => file.Path).ToArray();
        try
        {
            var names = _existing.Concat(SelectedProfiles).Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
            FtpRushSectionMigration.Merge([], SelectedSections, names, ReplaceExisting);
        }
        catch (Exception ex) { SummaryText.Text = ex.Message; return; }
        if (SelectedProfiles.Count == 0 && SelectedBookmarks.Count == 0 && SelectedSections.Count == 0 && SelectedVisionaryFiles.Count == 0)
        { SummaryText.Text = "Select sites, bookmarks, sections or a VISIONARY rules folder."; return; }
        DialogResult = true;
    }

    private sealed class ImportRow(FtpRushImportedSite site, bool duplicate) : INotifyPropertyChanged
    {
        private bool _passwordFromFile;
        public bool PasswordFromFile
        {
            get => _passwordFromFile;
            set { _passwordFromFile = value; PropertyChanged?.Invoke(this, new(nameof(PasswordStatus))); }
        }
        public string PasswordStatus => PasswordFromFile ? "From text file" : string.IsNullOrEmpty(Site.Profile.Password) ? "Not imported" : "Included";
        private bool _selected = !duplicate;
        public FtpRushImportedSite Site { get; } = site;
        public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public string Name => Site.Profile.Name;
        public string GroupPath => Site.GroupPath;
        public string Protocol => TransferProtocolNames.Display(Site.Profile.Protocol);
        public string Host => Site.Profile.Host;
        public int Port => Site.Profile.Port;
        public string RemotePath => Site.Profile.EffectiveOptions.BasePath;
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
