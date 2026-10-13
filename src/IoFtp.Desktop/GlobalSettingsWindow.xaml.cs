using System.Windows;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using IoFtp.Core.Models;
using IoFtp.Desktop.Models;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;

namespace IoFtp.Desktop;

public partial class GlobalSettingsWindow : Window
{
    private List<AdvancedSkipRule> _advancedSkipRules;
    private readonly ThemeSettings _originalTheme;
    private readonly bool _visionaryBridge;
    private readonly IrcSettings _ircSettings;
    private readonly LayoutSettingsEditor _layoutEditor;
    public GlobalSettings? Settings { get; private set; }
    private bool _backupBusy;
    private async void BackupExport_Click(object sender, RoutedEventArgs e)
    {
        if (_backupBusy) return;
        var password = BackupPasswordBox.Password;
        if (password != BackupPasswordConfirmBox.Password) { BackupStatusText.Text = "Backup passwords do not match."; return; }
        var picker = new SaveFileDialog { Filter = "FluxFTP backup (*.fluxbackup)|*.fluxbackup", DefaultExt = ".fluxbackup", AddExtension = true,
            FileName = "FluxFTP-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".fluxbackup" };
        if (picker.ShowDialog(this) != true) return;
        SetBackupBusy(true);
        try
        {
            var count = await Task.Run(() => BackupService.Current().Export(picker.FileName, password));
            BackupStatusText.Text = $"Backup exported: {count} files.\n{picker.FileName}";
            BackupPasswordBox.Clear(); BackupPasswordConfirmBox.Clear();
        }
        catch (Exception exception) { BackupStatusText.Text = "Export failed: " + exception.Message; }
        finally { SetBackupBusy(false); }
    }
    private async void BackupImport_Click(object sender, RoutedEventArgs e)
    {
        if (_backupBusy) return;
        var picker = new OpenFileDialog { Filter = "FluxFTP backup (*.fluxbackup;*.fluxrestore)|*.fluxbackup;*.fluxrestore" };
        if (picker.ShowDialog(this) != true) return;
        var password = BackupPasswordBox.Password;
        SetBackupBusy(true);
        try
        {
            var service = BackupService.Current();
            var package = await Task.Run(() => service.Read(picker.FileName, password));
            if (MessageBox.Show(this, $"Import {package.Files.Count} files from {package.Created.ToLocalTime():yyyy-MM-dd HH:mm}?\n\nThis replaces the saved configuration at the next startup. A recovery copy is saved first.",
                "Import FluxFTP backup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await Task.Run(() => service.StageImport(package));
            BackupPasswordBox.Clear(); BackupPasswordConfirmBox.Clear();
            BackupStatusText.Text = "Import prepared. Close FluxFTP completely and start it again to restore the backup.";
        }
        catch (System.Security.Cryptography.CryptographicException) { BackupStatusText.Text = "Import failed: incorrect password, damaged backup, or recovery copy belongs to another Windows user."; }
        catch (Exception exception) { BackupStatusText.Text = "Import failed: " + exception.Message; }
        finally { SetBackupBusy(false); }
    }
    private void BackupCancel_Click(object sender, RoutedEventArgs e)
    {
        try { BackupService.Current().CancelPendingImport(); BackupStatusText.Text = "Pending import cancelled."; }
        catch (Exception exception) { BackupStatusText.Text = exception.Message; }
    }
    private void SetBackupBusy(bool busy)
    {
        _backupBusy = busy; BackupExportButton.IsEnabled = !busy; BackupImportButton.IsEnabled = !busy; BackupCancelButton.IsEnabled = !busy;
        if (busy) BackupStatusText.Text = "Processing backup…";
    }
    public GlobalSettingsWindow(GlobalSettings s)
    {
        _visionaryBridge = s.EnableVisionaryBridge;
        _ircSettings = s.Irc ?? new();
        _advancedSkipRules = [.. s.AdvancedSkipRules ?? []];
        _originalTheme = s.Theme ?? ThemeManager.Default;
        InitializeComponent(); ProtocolBox.ItemsSource = Enum.GetValues<TransferProtocol>().Select(protocol => new ProtocolChoice(protocol)).ToArray(); ProxyTypeBox.ItemsSource = Enum.GetValues<ProxyType>(); LegendModeBox.ItemsSource = new[] { "Scrolling", "Static", "Activity", "Compact", "Hidden" };
        Closing += (_, args) => { if (_backupBusy) args.Cancel = true; };
        if (BackupService.Current().HasPendingImport) BackupStatusText.Text = "A backup import is pending. Close FluxFTP completely and start it again, or cancel the pending import.";
        BindBox.Text=s.BindAddress; PortFromBox.Text=$"{s.ActivePortFrom}"; PortToBox.Text=$"{s.ActivePortTo}"; ApiEnabledBox.IsChecked=s.EnableHttpsApi; ApiPortBox.Text=$"{s.HttpsApiPort}"; ApiLocalBox.IsChecked=s.ApiLocalhostOnly;
        ExpirationBox.Text=$"{s.PreparedJobExpirationSeconds}"; StarterBox.Text=$"{s.StarterTimeoutSeconds}"; RuntimeBox.Text=$"{s.MaxTransferRuntimeMinutes}"; JobHistoryBox.Text=$"{s.TransferJobHistory}"; TransferHistoryBox.Text=$"{s.TransferHistory}"; LogHistoryBox.Text=$"{s.LogBufferHistory}";
        UsernameBox.Text=s.DefaultUsername; ProtocolBox.SelectedItem=((ProtocolChoice[])ProtocolBox.ItemsSource).First(choice => choice.Protocol == s.DefaultProtocol); SlotsBox.Text=$"{s.DefaultSlots}"; UploadsBox.Text=$"{s.DefaultUploadSlots}"; DownloadsBox.Text=$"{s.DefaultDownloadSlots}"; DefaultIdleBox.Text=$"{s.DefaultIdleSeconds}";
        LocalPathBox.Text=s.LocalDownloadPath; LocalDownloadsBox.Text=$"{s.MaxLocalDownloadSlots}"; LocalUploadsBox.Text=$"{s.MaxLocalUploadSlots}";
        SerializeLocalBox.IsChecked = s.SerializeLocalTransfers;
        FxpSpeedMonitoringBox.IsChecked = s.EnableFxpSpeedMonitoring;
        DownloadMeasurementBox.IsChecked = s.EnableDownloadMeasurement;
        SkipSmallerTargetBox.IsChecked = s.FxpSkipSmallerTarget;
        SkipSameTargetBox.IsChecked = s.FxpSkipSameTarget;
        SkipLargerTargetBox.IsChecked = s.FxpSkipLargerTarget;
        PriorityPatternsBox.Text=s.PriorityPatterns;
        foreach (var category in TransferFilePriority.Categories(s.TransferCategoryOrder)) PriorityCategoriesList.Items.Add(category);
        PriorityCategoriesList.SelectedIndex = 0;
        SkipPatternsBox.Text=s.SkipPatterns;
        EnableSkipListBox.IsChecked=s.EnableSkipList; SkipZeroByteBox.IsChecked=s.SkipZeroByteFiles;
        SkipEmptyDirectoryBox.IsChecked=s.SkipEmptyDirectories; ApplySkipFoldersBox.IsChecked=s.ApplySkipListToFolders;
        SkipModeBox.ItemsSource = new[] { "Both", "Auto", "Manual" }; SkipModeBox.SelectedItem=s.SkipTransferMode;
        SkippedColorBox.Text=s.SkippedItemColor;
        ApiPasswordBox.Password=s.ApiPassword;
        MinimizeToTrayBox.IsChecked=s.MinimizeToTray;
        LegendModeBox.SelectedItem=s.LegendBarMode; if (LegendModeBox.SelectedIndex < 0) LegendModeBox.SelectedItem="Compact";
        ProxyTypeBox.SelectedItem=s.ProxyType; ProxyHostBox.Text=s.ProxyHost; ProxyPortBox.Text=$"{s.ProxyPort}"; ProxyUsernameBox.Text=s.ProxyUsername; ProxyPasswordBox.Password=s.ProxyPassword; ProxyDnsBox.IsChecked=s.ProxyDns; ProxyDataBox.IsChecked=s.ProxyDataConnections;
        CheckUpdatesBox.IsChecked=s.CheckForUpdatesAtStartup;
        UpdateStatusText.Text=$"Installed version: {UpdateCheckService.CurrentVersion}";
        LoadTheme(_originalTheme);
        _layoutEditor = new LayoutSettingsEditor(LayoutPanel, s.Layout);

        Closing += RestoreThemeWhenCanceled;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_backupBusy) return;
        ErrorText.Text = "";
        try { _ = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(SkippedColorBox.Text); }
        catch { ErrorText.Text="Choose a valid skipped-item color, such as #7F9FBF."; return; }
        Dictionary<string, ViewLayoutSettings> layout;
        try { layout = _layoutEditor.Read(); }
        catch (ArgumentException exception) { ErrorText.Text = exception.Message; return; }
        var boxes = new[] { PortFromBox, PortToBox, ApiPortBox, ExpirationBox, StarterBox, RuntimeBox, JobHistoryBox, TransferHistoryBox, LogHistoryBox, SlotsBox, UploadsBox, DownloadsBox, DefaultIdleBox, LocalDownloadsBox, LocalUploadsBox, ProxyPortBox };
        if (boxes.Any(box => !int.TryParse(box.Text, out _))) { ErrorText.Text="All numeric settings must be whole numbers."; return; }
        int N(System.Windows.Controls.TextBox b)=>int.Parse(b.Text);
        if (N(PortFromBox) is <1 or >65535 || N(PortToBox)<N(PortFromBox) || N(SlotsBox)<1 || N(UploadsBox)<0 || N(DownloadsBox)<0 || N(UploadsBox)>N(SlotsBox) || N(DownloadsBox)>N(SlotsBox)) { ErrorText.Text="Port range or slot limits are invalid."; return; }
        if (ApiEnabledBox.IsChecked == true && string.IsNullOrWhiteSpace(ApiPasswordBox.Password)) { ErrorText.Text="API password is required when the API is enabled."; return; }
        if ((ProxyType)(ProxyTypeBox.SelectedItem ?? ProxyType.None) != ProxyType.None && (string.IsNullOrWhiteSpace(ProxyHostBox.Text) || N(ProxyPortBox) is < 1 or > 65535)) { ErrorText.Text="Proxy host or port is invalid."; return; }
        var theme = ReadTheme();
        var irc = _ircSettings;
        if (irc is null) return;
        if (!ThemeManager.TryValidate(theme, out var themeError)) { ErrorText.Text = themeError; return; }
        Settings = new GlobalSettings(BindBox.Text.Trim(),N(PortFromBox),N(PortToBox),ApiEnabledBox.IsChecked==true,N(ApiPortBox),ApiLocalBox.IsChecked==true,N(ExpirationBox),N(StarterBox),N(RuntimeBox),N(JobHistoryBox),N(TransferHistoryBox),N(LogHistoryBox),UsernameBox.Text.Trim(),N(SlotsBox),N(UploadsBox),N(DownloadsBox),((ProtocolChoice)ProtocolBox.SelectedItem).Protocol,N(DefaultIdleBox),LocalPathBox.Text.Trim(),N(LocalDownloadsBox),N(LocalUploadsBox),PriorityPatternsBox.Text.Trim(),SkipPatternsBox.Text.Trim(),ApiPasswordBox.Password,MinimizeToTrayBox.IsChecked==true,LegendModeBox.SelectedItem?.ToString() ?? "Compact",(ProxyType)(ProxyTypeBox.SelectedItem ?? ProxyType.None),ProxyHostBox.Text.Trim(),N(ProxyPortBox),ProxyUsernameBox.Text.Trim(),ProxyPasswordBox.Password,ProxyDnsBox.IsChecked==true,ProxyDataBox.IsChecked==true,CheckUpdatesBox.IsChecked==true, _advancedSkipRules.ToArray(), theme, irc);
        Settings = Settings with { EnableVisionaryBridge = _visionaryBridge, SerializeLocalTransfers = SerializeLocalBox.IsChecked == true,
            TransferCategoryOrder = string.Join("\n", PriorityCategoriesList.Items.Cast<string>()),
            EnableFxpSpeedMonitoring = FxpSpeedMonitoringBox.IsChecked == true, Layout = layout, MetadataSkipDefaultsApplied = true, VideoMetadataSkipDefaultsApplied = true,
            EnableDownloadMeasurement = DownloadMeasurementBox.IsChecked == true,
            FxpSkipSmallerTarget = SkipSmallerTargetBox.IsChecked == true,
            FxpSkipSameTarget = SkipSameTargetBox.IsChecked == true,
            FxpSkipLargerTarget = SkipLargerTargetBox.IsChecked == true,
            EnableSkipList=EnableSkipListBox.IsChecked==true, SkipZeroByteFiles=SkipZeroByteBox.IsChecked==true,
            SkipEmptyDirectories=SkipEmptyDirectoryBox.IsChecked==true, ApplySkipListToFolders=ApplySkipFoldersBox.IsChecked==true,
            SkipTransferMode=SkipModeBox.SelectedItem?.ToString() ?? "Both", SkippedItemColor=SkippedColorBox.Text.Trim() };
        try { new GlobalSettingsStore().Save(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { Settings = null; ErrorText.Text = "Could not save settings to disk. Check write access and free space; your edits are still here. Please try Save again."; return; }
        ThemeManager.Apply(theme); DialogResult = true;
    }
    private void PriorityCategoryUp_Click(object sender, RoutedEventArgs e) => MovePriorityCategory(-1);
    private void PriorityCategoryDown_Click(object sender, RoutedEventArgs e) => MovePriorityCategory(1);
    private void MovePriorityCategory(int delta)
    {
        var index = PriorityCategoriesList.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= PriorityCategoriesList.Items.Count) return;
        var category = PriorityCategoriesList.Items[index];
        PriorityCategoriesList.Items.RemoveAt(index);
        PriorityCategoriesList.Items.Insert(target, category);
        PriorityCategoriesList.SelectedIndex = target;
    }

    private ThemeSettings ReadTheme() => new(
        ThemeNameBox.Text.Trim(), ThemeWindowBox.Text.Trim(), ThemeSurfaceBox.Text.Trim(), ThemeRaisedBox.Text.Trim(), ThemeBorderBox.Text.Trim(),
        ThemeAccentBox.Text.Trim(), ThemeAccentStrongBox.Text.Trim(), ThemeTextBox.Text.Trim(), ThemeMutedTextBox.Text.Trim(), ThemeSelectionBox.Text.Trim(), ThemeHoverBox.Text.Trim(),
        ThemeFontBox.Text.Trim(), ThemeMonoFontBox.Text.Trim(), double.TryParse(ThemeFontSizeBox.Text, out var size) ? size : 0);

    private void LoadTheme(ThemeSettings theme)
    {
        ThemeNameBox.Text=theme.Name; ThemeWindowBox.Text=theme.Window; ThemeSurfaceBox.Text=theme.Surface; ThemeRaisedBox.Text=theme.SurfaceRaised; ThemeBorderBox.Text=theme.Border;
        ThemeAccentBox.Text=theme.Accent; ThemeAccentStrongBox.Text=theme.AccentStrong; ThemeTextBox.Text=theme.Text; ThemeMutedTextBox.Text=theme.MutedText;
        ThemeSelectionBox.Text=theme.Selection; ThemeHoverBox.Text=theme.Hover; ThemeFontBox.Text=theme.FontFamily; ThemeMonoFontBox.Text=theme.MonospaceFontFamily; ThemeFontSizeBox.Text=$"{theme.FontSize:0.##}";
    }

    private void PreviewTheme_Click(object sender, RoutedEventArgs e)
    {
        var theme = ReadTheme();
        if (!ThemeManager.TryValidate(theme, out var error)) { ErrorText.Text=error; return; }
        ThemeManager.Apply(theme); ErrorText.Text=$"Previewing theme: {theme.Name}";
    }

    private void ResetTheme_Click(object sender, RoutedEventArgs e) { LoadTheme(ThemeManager.Default); ThemeManager.Apply(ThemeManager.Default); ErrorText.Text="Default theme restored in preview."; }

    private void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title="Import FluxFTP theme", Filter="FluxFTP theme (*.flux-theme.json)|*.flux-theme.json|JSON files (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var theme = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("Theme file is empty.");
            if (!ThemeManager.TryValidate(theme, out var error)) throw new InvalidDataException(error);
            LoadTheme(theme); ThemeManager.Apply(theme); ErrorText.Text=$"Imported theme: {theme.Name}";
        }
        catch (Exception exception) { ErrorText.Text=$"Theme import failed: {exception.Message}"; }
    }

    private void ExportTheme_Click(object sender, RoutedEventArgs e)
    {
        var theme = ReadTheme();
        if (!ThemeManager.TryValidate(theme, out var error)) { ErrorText.Text=error; return; }
        var safeName = string.Concat((string.IsNullOrWhiteSpace(theme.Name) ? "FluxFTP-theme" : theme.Name).Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var dialog = new SaveFileDialog { Title="Export FluxFTP theme", Filter="FluxFTP theme (*.flux-theme.json)|*.flux-theme.json", FileName=$"{safeName}.flux-theme.json", AddExtension=true };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(theme, new JsonSerializerOptions { WriteIndented=true })); ErrorText.Text=$"Theme exported: {dialog.FileName}"; }
        catch (Exception exception) { ErrorText.Text=$"Theme export failed: {exception.Message}"; }
    }

    private void RestoreThemeWhenCanceled(object? sender, CancelEventArgs e)
    {
        if (DialogResult != true) ThemeManager.Apply(_originalTheme);
    }
    private SkipListPackage ReadSkipPackage() => new(SkipPatternsBox.Text, _advancedSkipRules.ToArray(),
        EnableSkipListBox.IsChecked==true, SkipZeroByteBox.IsChecked==true, SkipEmptyDirectoryBox.IsChecked==true,
        ApplySkipFoldersBox.IsChecked==true, SkipModeBox.SelectedItem?.ToString() ?? "Both", SkippedColorBox.Text.Trim());

    private void SkippedColor_Click(object sender, RoutedEventArgs e)
    {
        using var picker = new System.Windows.Forms.ColorDialog();
        if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SkippedColorBox.Text=$"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
    }

    private void ImportSkipList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title="Import skip list", Filter="FluxFTP skip list (*.flux-skip.json)|*.flux-skip.json|All JSON files (*.json)|*.json" };
        if (dialog.ShowDialog(this)!=true) return;
        try
        {
            var package=JsonSerializer.Deserialize<SkipListPackage>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("Empty skip list.");
            package.Validate();
            _ = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(package.Color);
            SkipPatternsBox.Text=package.Patterns; _advancedSkipRules=[.. package.Rules ?? []];
            EnableSkipListBox.IsChecked=package.Enabled; SkipZeroByteBox.IsChecked=package.ZeroByteFiles;
            SkipEmptyDirectoryBox.IsChecked=package.EmptyDirectories; ApplySkipFoldersBox.IsChecked=package.ApplyToFolders;
            SkipModeBox.SelectedItem=package.Mode; SkippedColorBox.Text=package.Color;
            ErrorText.Text="Skip list imported into this dialog. Save to apply.";
        }
        catch (Exception exception) { ErrorText.Text="Skip list import failed: "+exception.Message; }
    }

    private void ExportSkipList_Click(object sender, RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog { Title="Export skip list", Filter="FluxFTP skip list (*.flux-skip.json)|*.flux-skip.json", FileName="FluxFTP.flux-skip.json", AddExtension=true };
        if(dialog.ShowDialog(this)!=true) return;
        try
        {
            var package=ReadSkipPackage(); package.Validate();
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(package,new JsonSerializerOptions { WriteIndented=true }));
            ErrorText.Text="Skip list exported.";
        }
        catch(Exception exception) { ErrorText.Text="Skip list export failed: "+exception.Message; }
    }

    private void AdvancedSkiplist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AdvancedSkiplistWindow(_advancedSkipRules) { Owner = this };
        if (dialog.ShowDialog() == true) _advancedSkipRules = [.. dialog.Rules];
    }
    private async void TestProxy_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ProxyPortBox.Text, out var port)) { ErrorText.Text="Proxy port is invalid."; return; }
        var proxy = new ProxyConfiguration((ProxyType)(ProxyTypeBox.SelectedItem ?? ProxyType.None), ProxyHostBox.Text.Trim(), port, ProxyUsernameBox.Text.Trim(), ProxyPasswordBox.Password, ProxyDnsBox.IsChecked==true, ProxyDataBox.IsChecked==true);
        if (proxy.Type == ProxyType.None) { ErrorText.Text="Select a proxy type first."; return; }
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); using var client = await ProxyConnector.ConnectAsync("example.com", 443, proxy, timeout.Token); ErrorText.Text="Proxy test succeeded."; }
        catch (Exception exception) { ErrorText.Text=$"Proxy test failed: {exception.Message}"; }
    }
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text="Checking GitHub Releases…";
        var result = await new UpdateCheckService().CheckAsync(true);
        UpdateStatusText.Text = result.Error is not null && string.IsNullOrEmpty(result.LatestVersion)
            ? $"Update check failed: {result.Error}"
            : result.UpdateAvailable
                ? $"Update available: FluxFTP {result.LatestVersion}\n{result.ReleaseUrl}"
                : $"Latest version installed ({result.CurrentVersion}).";
    }
    private sealed record ProtocolChoice(TransferProtocol Protocol)
    {
        public override string ToString() => TransferProtocolNames.Display(Protocol);
    }
}
