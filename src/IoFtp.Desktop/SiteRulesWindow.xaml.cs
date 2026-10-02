using System.Diagnostics;
using System.IO;
using System.Windows;
using IoFtp.Desktop.Services;
using Microsoft.Win32;
using System.Windows.Controls;

namespace IoFtp.Desktop;

public partial class SiteRulesWindow : Window
{
    private VisionaryRuleText? _document;
    private bool _selecting;
    private static string FolderSetting => Path.Combine(AppContext.BaseDirectory, "FluxFTP-visionary-rules-folder.txt");
    public SiteRulesWindow()
    {
        InitializeComponent();
        FolderText.Text = SiteRuleStore.DefaultDirectory;
        Reload();
        Closing += (_, e) => { if (!DiscardEdits()) e.Cancel = true; };
        try
        {
            var folder = File.Exists(FolderSetting) ? File.ReadAllText(FolderSetting).Trim() : null;
            if (folder is null)
                folder = VisionaryImportInventory.Load().Select(path => Path.GetDirectoryName(path)!)
                    .Select(parent => Path.Combine(parent, "RULES")).FirstOrDefault(Directory.Exists);
            if (folder is not null) LoadVisionaryFolder(folder);
        }
        catch (Exception ex) { VisionaryRuleStatus.Text = ex.Message; }
    }
    private bool DiscardEdits() => _document is null || VisionaryRuleEditor.Text == _document.Text ||
        MessageBox.Show(this, "Discard unsaved changes to the site rule text?", "Unsaved rules", MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void LoadVisionaryFolder(string folder)
    {
        var resolved = VisionaryRuleText.ResolveFolder(folder);
        var files = Directory.EnumerateFiles(resolved, "*.txt").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path)).ToArray();
        _document = null;
        VisionaryRuleEditor.Clear(); VisionaryRuleEditor.IsReadOnly = true;
        VisionaryFolderText.Text = resolved;
        VisionaryRuleFiles.ItemsSource = files;
        VisionaryRuleStatus.Text = $"{files.Length} site rule text files.";
        VisionaryRulesTab.IsSelected = true;
        if (files.Length > 0) VisionaryRuleFiles.SelectedIndex = 0;
    }
    private void ChooseVisionaryRules_Click(object sender, RoutedEventArgs e)
    {
        if (!DiscardEdits()) return;
        var picker = new OpenFolderDialog { Title = "Select VISIONARY, User_Files or RULES" };
        if (picker.ShowDialog(this) != true) return;
        try { LoadVisionaryFolder(picker.FolderName); File.WriteAllText(FolderSetting, VisionaryFolderText.Text); }
        catch (Exception ex) { VisionaryRuleStatus.Text = ex.Message; }
    }
    private void VisionaryRuleFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selecting) return;
        if (!DiscardEdits())
        {
            _selecting = true;
            VisionaryRuleFiles.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            _selecting = false; return;
        }
        ReadVisionaryRule();
    }
    private void ReadVisionaryRule()
    {
        _document = null; VisionaryRuleEditor.Clear(); VisionaryRuleEditor.IsReadOnly = true;
        if (VisionaryRuleFiles.SelectedItem is not FileInfo file) return;
        try
        {
            _document = new VisionaryRuleText(file.FullName);
            VisionaryRuleEditor.Text = _document.Text; VisionaryRuleEditor.IsReadOnly = false;
            VisionaryRuleStatus.Text = "Editing " + file.Name;
        }
        catch (Exception ex) { VisionaryRuleStatus.Text = ex.Message; }
    }
    private void ReloadVisionaryRule_Click(object sender, RoutedEventArgs e)
    { if (DiscardEdits()) ReadVisionaryRule(); }
    private void SaveVisionaryRule_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        try { VisionaryRuleStatus.Text = _document.Save(VisionaryRuleEditor.Text); }
        catch (Exception ex) { VisionaryRuleStatus.Text = ex.Message; }
    }
    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();
    private void Reload()
    {
        try
        {
            var files = new SiteRuleStore().Load();
            RulesGrid.ItemsSource = files.SelectMany(f => f.Sections.Select(s => new Row(f.Site, s.Name, s.Path, s.RequiresMetadata))).ToArray();
            ResultText.Text = $"Loaded {files.Count} site rule file(s). No active files means existing transfer behavior is unchanged.";
        }
        catch (Exception ex) { RulesGrid.ItemsSource = null; ResultText.Text = $"Invalid rules: {ex.Message}"; }
    }
    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not Row row) { ResultText.Text = "Select a section first."; return; }
        var result = new SiteRuleStore().Evaluate(row.Site, row.Section, ReleaseBox.Text.Trim());
        ResultText.Text = $"{(result.Accepted ? "ALLOW" : "DROP")}: {result.Message}";
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(SiteRuleStore.DefaultDirectory); Process.Start(new ProcessStartInfo(SiteRuleStore.DefaultDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { ResultText.Text = ex.Message; }
    }
    private sealed record Row(string Site, string Section, string Path, bool Metadata);
}
