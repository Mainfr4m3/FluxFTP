using System.Diagnostics;
using System.IO;
using System.Windows;
using IoFtp.Desktop.Services;

namespace IoFtp.Desktop;

public partial class SiteRulesWindow : Window
{
    public SiteRulesWindow()
    {
        InitializeComponent();
        FolderText.Text = SiteRuleStore.DefaultDirectory;
        Reload();
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
