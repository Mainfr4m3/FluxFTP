using System.Collections.ObjectModel;
using System.Windows;
using IoFtp.Desktop.Services;
using Microsoft.Win32;

namespace IoFtp.Desktop;

public partial class SectionsWindow : Window
{
    private readonly SectionStore _store = new();
    private readonly ObservableCollection<SectionRow> _rows = [];

    public SectionsWindow()
    {
        InitializeComponent();
        ValidationModeColumn.ItemsSource = Enum.GetValues<SectionValidationMode>();
        foreach (var section in _store.Load())
            foreach (var site in section.SitePaths.DefaultIfEmpty(new KeyValuePair<string, string>("ioFTPD", "/")))
                _rows.Add(new(section.Name, site.Key, site.Value, section.Hotkey, section.AllowPatterns, section.DenyPatterns, section.ValidationMode));
        SectionsGrid.ItemsSource = _rows;
    }

    private void Add_Click(object sender, RoutedEventArgs e) { var row = new SectionRow("New section", "ioFTPD", "/", 0, "", "", SectionValidationMode.Disabled); _rows.Add(row); SectionsGrid.SelectedItem = row; SectionsGrid.ScrollIntoView(row); }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (SectionsGrid.SelectedItem is SectionRow row) _rows.Remove(row); }
    private void ImportSections_Click(object sender, RoutedEventArgs e)
    {
        SectionsGrid.CommitEdit(); SectionsGrid.CommitEdit();
        var dialog = new OpenFileDialog
        {
            Title = "Import FluxFTP Sections",
            Filter = "Sections (*.json;*.xml)|*.json;*.xml|FluxFTP sections (*.json)|*.json|CAPTION/REMOTE XML (*.xml)|*.xml|All files (*.*)|*.*",
            InitialDirectory = AppContext.BaseDirectory,
            FileName = "FluxFTP-sections.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = _store.Import(dialog.FileName);
            _rows.Clear();
            foreach (var section in imported)
            foreach (var site in section.SitePaths.DefaultIfEmpty(new KeyValuePair<string, string>("ioFTPD", "/")))
                _rows.Add(new(section.Name, site.Key, site.Value, section.Hotkey, section.AllowPatterns,
                    section.DenyPatterns, section.ValidationMode));
            MessageBox.Show($"Imported {imported.Count} sections. Review them and press Save to apply the changes.",
                "Import Sections", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Could not import sections: {exception.Message}", "Import Sections",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void TestPrecheck_Click(object sender, RoutedEventArgs e)
    {
        SectionsGrid.CommitEdit(); SectionsGrid.CommitEdit();
        if (SectionsGrid.SelectedItem is not SectionRow row) { MessageBox.Show("Select a section first.", "Section precheck"); return; }
        _store.Save(_rows.Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Site))
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new SectionDefinition(group.Key,
                group.GroupBy(item => item.Site.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(site => site.Key, site => site.Last().Path.Trim(), StringComparer.OrdinalIgnoreCase),
                group.First().Hotkey, group.First().AllowPatterns.Trim(), group.First().DenyPatterns.Trim(), group.First().ValidationMode)));
        var prompt = new CommandParameterWindow("Release name to validate") { Owner = this };
        if (prompt.ShowDialog() != true) return;
        var result = SectionReleaseValidator.Validate(row.Name, prompt.Value.Trim());
        MessageBox.Show(result.Message, result.Accepted ? "Precheck passed" : $"Precheck {result.Mode}",
            MessageBoxButton.OK, result.Accepted ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void SiteRules_Click(object sender, RoutedEventArgs e) => new SiteRulesWindow { Owner = this }.ShowDialog();
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SectionsGrid.CommitEdit(); SectionsGrid.CommitEdit();
        var sections = _rows.Where(row => !string.IsNullOrWhiteSpace(row.Name) && !string.IsNullOrWhiteSpace(row.Site))
            .GroupBy(row => row.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new SectionDefinition(group.Key,
                group.GroupBy(row => row.Site.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(site => site.Key, site => site.Last().Path.Trim(), StringComparer.OrdinalIgnoreCase),
                group.First().Hotkey, group.First().AllowPatterns.Trim(), group.First().DenyPatterns.Trim(), group.First().ValidationMode)).ToList();
        _store.Save(sections);
        MessageBox.Show("Sections saved.", "Sections", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private sealed class SectionRow(string name, string site, string path, int hotkey, string allowPatterns, string denyPatterns, SectionValidationMode validationMode)
    {
        public string Name { get; set; } = name; public string Site { get; set; } = site;
        public string Path { get; set; } = path; public int Hotkey { get; set; } = hotkey;
        public string AllowPatterns { get; set; } = allowPatterns; public string DenyPatterns { get; set; } = denyPatterns;
        public SectionValidationMode ValidationMode { get; set; } = validationMode;
    }
}
