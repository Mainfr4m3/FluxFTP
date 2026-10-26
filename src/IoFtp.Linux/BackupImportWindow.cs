using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using System.Security.Cryptography;
namespace IoFtp.Linux;
internal sealed class BackupImportWindow : Window
{
    private readonly LinuxConfiguration _configuration = new();
    public bool Imported { get; private set; }
    public BackupImportWindow()
    {
        Title = "Import Windows backup"; Width = 720; Height = 560;
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Select a password-protected .fluxbackup exported from FluxFTP Windows.\nSites, portable passwords, sections and compatible settings are imported.\nVisi integration stays disabled.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var path = new TextBox { Watermark = "Windows .fluxbackup file" };
        panel.Children.Add(path);
        panel.Children.Add(Ui.Button("Browse...", async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Windows FluxFTP backup", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("FluxFTP backup") { Patterns = ["*.fluxbackup"] }] });
            if (files.Count > 0) path.Text = files[0].TryGetLocalPath();
        }));
        var password = new TextBox { PasswordChar = '*', Watermark = "Backup password" }; panel.Children.Add(password);
        panel.Children.Add(new TextBlock { Text = "Linux local download folder (replaces Windows drive paths):" });
        var local = new TextBox { Text = _configuration.Load().Options.DownloadPath }; panel.Children.Add(local);
        var report = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; panel.Children.Add(report);
        ImportPlan? plan = null;
        var apply = Ui.Button("Import confirmed settings", () =>
        {
            if (plan is null) return Task.CompletedTask;
            try { _configuration.Save(plan.State); Imported = true; password.Text = ""; Close(); }
            catch (Exception ex) { report.Text = ex.Message; }
            return Task.CompletedTask;
        });
        apply.IsEnabled = false;
        var inspect = Ui.Button("Read backup", async () =>
        {
            plan = null; apply.IsEnabled = false; report.Text = "Reading backup...";
            path.IsEnabled = password.IsEnabled = local.IsEnabled = false;
            try
            {
                var source = path.Text ?? ""; var secret = password.Text ?? ""; var destination = local.Text ?? ""; var current = _configuration.Load();
                plan = await Task.Run(() => WindowsBackupImport.Read(source, secret, destination, current));
                report.Text = plan.Summary; apply.IsEnabled = true;
            }
            catch (Exception ex) { report.Text = ex.Message; }
            finally { path.IsEnabled = password.IsEnabled = local.IsEnabled = true; }
        });
        void Invalidate() { plan = null; apply.IsEnabled = false; }
        path.TextChanged += (_, _) => Invalidate(); password.TextChanged += (_, _) => Invalidate(); local.TextChanged += (_, _) => Invalidate();
        panel.Children.Add(inspect); panel.Children.Add(apply); panel.Children.Add(Ui.Button("Cancel", () => { Close(); return Task.CompletedTask; }));
        Content = new ScrollViewer { Content = panel };
    }
}
