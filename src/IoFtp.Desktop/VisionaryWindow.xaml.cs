using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IoFtp.Desktop.Services;
using IoFtp.Desktop.Models;
using Microsoft.Win32;

namespace IoFtp.Desktop;

public partial class VisionaryWindow : Window
{
    private readonly ObservableCollection<VisionaryConfiguration> _files = [];
    private readonly Func<bool, Task> _setBridge;
    private readonly Func<IrcSettings, Task> _saveIrc;
    private readonly Action _openRaceLog;
    private bool _ready;
    private readonly string _indexPath = Path.Combine(AppContext.BaseDirectory, "FluxFTP-visionary-files.json");

    internal VisionaryWindow(bool enabled, Func<bool, Task> setBridge, IrcSettings irc, Func<IrcSettings, Task> saveIrc, Action openRaceLog)
    {
        _setBridge = setBridge;
        _saveIrc = saveIrc;
        _openRaceLog = openRaceLog;
        InitializeComponent();
        IrcEditor.Load(irc);
        FilesBox.ItemsSource = _files;
        BridgeBox.IsChecked = enabled;
        HelpBox.Text = "1. Import the VISIONARY User_Files folder (or individual INI / CHA files).\n" +
            "2. Import FTPRush sites, bookmarks and section paths together; review conflicts in the Sections tab. Include your VISIONARY folder in the Rules tab.\n" +
            "3. Export the bridge, load Visionary-FluxFTP.mrc after Visionary.mrc, and set [FU_FXP] USE_FU_FXP=1 in options.ini.\n" +
            "mIRC: /fluxvisi test   •   /fluxvisi transfer SECTION RELEASE SOURCE TARGET\n" +
            "Races rescan growing releases, then recopy files once after COMPLETE to verify the final version. NUKE stops the race. Disable the bridge to stop all watches. Settings apply to new races. Other Rush-specific options remain VISIONARY configuration.";
        try
        {
            var options = VisionaryRaceOptions.Load();
            RushOptionsBox.Text = options.RushOptionsPath;
            CompleteFlagBox.Text = options.CompleteFlag; RefreshBox.Text = options.RefreshSeconds.ToString(); TimeoutBox.Text = options.TimeoutMinutes.ToString();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        _ready = true;
        try
        {
            if (File.Exists(_indexPath)) Import(JsonSerializer.Deserialize<string[]>(File.ReadAllText(_indexPath)) ?? []);
        }
        catch (Exception ex) { StatusText.Text = $"Could not restore imported files: {ex.Message}"; }
        Closing += (_, args) =>
        {
            EntriesGrid.CommitEdit(); EntriesGrid.CommitEdit();
            if (_files.Any(file => file.HasChanges)) args.Cancel = MessageBox.Show(this,
                "Discard unsaved VISIONARY edits?", "Unsaved edits", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes;
        };
    }

    private async void SaveIrc_Click(object sender, RoutedEventArgs e)
    {
        var settings = IrcEditor.Read();
        if (settings is null) return;
        try { await _saveIrc(settings); MessageBox.Show(this, "IRC settings saved. Connection status appears in the main log.", "IRC Add-ons"); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Save IRC settings", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void RaceLog_Click(object sender, RoutedEventArgs e) => _openRaceLog();

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Import VISIONARY configuration", Multiselect = true,
            Filter = "VISIONARY / section configuration|*.ini;*.cha;*.txt|All files|*.*" };
        if (picker.ShowDialog(this) == true) Import(picker.FileNames);
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Select VISIONARY or User_Files folder" };
        if (picker.ShowDialog(this) != true) return;
        var root = Directory.Exists(Path.Combine(picker.FolderName, "User_Files")) ? Path.Combine(picker.FolderName, "User_Files") : picker.FolderName;
        try
        {
            var paths = Directory.EnumerateFiles(root).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".ini" or ".cha").ToList();
            var sites = Path.Combine(root, "inifiles");
            if (Directory.Exists(sites)) paths.AddRange(Directory.EnumerateFiles(sites, "*.ini"));
            Import(paths);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Import(IEnumerable<string> paths)
    {
        var errors = new List<string>(); var count = 0;
        foreach (var path in paths)
        {
            if (_files.Any(f => f.Path.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))) continue;
            try { _files.Add(new(path)); count++; }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        FilesBox.SelectedItem ??= _files.FirstOrDefault();
        StatusText.Text = $"Loaded {count} file(s). Edits remain in memory until Save. " + string.Join("\n", errors);
        try
        {
            var temporary = _indexPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_files.Select(file => file.Path)));
            File.Move(temporary, _indexPath, true);
        }
        catch (Exception ex) { StatusText.Text += $" Could not remember file list: {ex.Message}"; }
    }

    private void File_Changed(object sender, SelectionChangedEventArgs e)
    {
        EntriesGrid.ItemsSource = (FilesBox.SelectedItem as VisionaryConfiguration)?.Entries;
        ApplyFilter();
    }
    private void Filter_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void ApplyFilter()
    {
        if (EntriesGrid?.ItemsSource is null) return;
        var search = FilterBox.Text.Trim();
        CollectionViewSource.GetDefaultView(EntriesGrid.ItemsSource).Filter = value => value is VisionaryConfiguration.Entry row &&
            (row.Section.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Key.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Value.Contains(search, StringComparison.OrdinalIgnoreCase));
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        EntriesGrid.CommitEdit(); EntriesGrid.CommitEdit();
        if (FilesBox.SelectedItem is not VisionaryConfiguration file) return;
        try { StatusText.Text = file.Save(); }
        catch (Exception ex) { StatusText.Text = $"Save failed: {ex.Message}"; }
    }
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        EntriesGrid.CommitEdit(); EntriesGrid.CommitEdit();
        if (FilesBox.SelectedItem is not VisionaryConfiguration file) return;
        if (file.HasChanges && MessageBox.Show(this, "Discard edits to this file and reload it?", "Reload configuration",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var replacement = new VisionaryConfiguration(file.Path);
            _files[_files.IndexOf(file)] = replacement; FilesBox.SelectedItem = replacement;
            StatusText.Text = "Reloaded selected file.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void SaveRace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new VisionaryRaceOptions(CompleteFlagBox.Text, int.Parse(RefreshBox.Text), int.Parse(TimeoutBox.Text), RushOptionsBox.Text.Trim()).Save();
            StatusText.Text = "Race settings saved for new races. Existing watches keep their current settings.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void ChooseRushOptions_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Select sample/covers transfer profiles", Filter = "Rush options|rushopt.ini|INI files|*.ini" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            VisionaryTransferProfiles.Load(picker.FileName);
            RushOptionsBox.Text = picker.FileName;
            StatusText.Text = "Sample/covers profiles validated. Save race settings to use them for new races. Other Rush options are not changed.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not load sample/covers profiles: {ex.Message}"; }
    }
    private void UseFlag_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesGrid.SelectedItem is VisionaryConfiguration.Entry row && row.Key.Equals("completeflag", StringComparison.OrdinalIgnoreCase))
        {
            if ((row.Section.Equals("sample", StringComparison.OrdinalIgnoreCase) || row.Section.Equals("covers", StringComparison.OrdinalIgnoreCase)) &&
                FilesBox.SelectedItem is VisionaryConfiguration file)
            { RushOptionsBox.Text = file.Path; StatusText.Text = "Selected the sample/covers profile file. Save race settings to apply it; the main release completion regex stays separate."; }
            else { CompleteFlagBox.Text = row.Value; StatusText.Text = "Copied for review. Save race settings validates the regex before applying it."; }
        }
        else StatusText.Text = "Select a completeflag setting first.";
    }
    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesGrid.SelectedItem is not VisionaryConfiguration.Entry row || !row.Key.Equals("completeflag", StringComparison.OrdinalIgnoreCase))
        { StatusText.Text = "Select a completeflag setting in rushopt.ini first."; return; }
        var prompt = new CommandParameterWindow("Directory listing entry to test against completeflag") { Owner = this };
        if (prompt.ShowDialog() != true) return;
        try
        {
            var regex = new Regex(row.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            StatusText.Text = regex.IsMatch("") ? "Pattern matches empty text: it cannot reliably detect completion." :
                regex.IsMatch(prompt.Value) ? "MATCH using .NET regex. This test does not mark a transfer complete." : "NO MATCH using .NET regex.";
        }
        catch (ArgumentException ex) { StatusText.Text = $"Invalid .NET regex: {ex.Message}"; }
        catch (RegexMatchTimeoutException) { StatusText.Text = "Pattern took too long to evaluate."; }
    }
    private async void Bridge_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        try { await _setBridge(BridgeBox.IsChecked == true); StatusText.Text = "Bridge setting saved. See the FluxFTP log for listener status."; }
        catch (Exception ex) { StatusText.Text = $"Bridge failed: {ex.Message}"; }
    }
    private void Rush_Click(object sender, RoutedEventArgs e)
    {
        var window = new SiteManagerWindow { Owner = this };
        window.Loaded += (_, _) => window.ImportFtpRush();
        window.ShowDialog();
        try { Import(VisionaryImportInventory.Load()); }
        catch (Exception ex) { StatusText.Text = $"Could not load imported rules: {ex.Message}"; }
    }
    private void Sections_Click(object sender, RoutedEventArgs e) => new SectionsWindow { Owner = this }.ShowDialog();
    private void ExportBridge_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Export mIRC bridge to folder" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            BridgeBundle.Export(picker.FolderName);
            StatusText.Text = $"Exported FluxFTP and VISIONARY bridges. In mIRC: /load -rs \"{Path.Combine(picker.FolderName, "visionary", "Visionary-FluxFTP.mrc")}\" then /fluxvisi test";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
