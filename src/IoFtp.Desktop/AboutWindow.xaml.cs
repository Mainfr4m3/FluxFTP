using System.Windows;
using IoFtp.Desktop.Services;

namespace IoFtp.Desktop;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        // Keep the branding in the content header rather than the native title bar.
        Icon = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4);
        VersionText.Text = $"Version {UpdateCheckService.CurrentVersion}";
    }
    private void GitHub_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Mainfr4m3/FluxFTP") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, "Could not open the browser. Visit https://github.com/Mainfr4m3/FluxFTP", "GitHub", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void Changelog_Click(object sender, RoutedEventArgs e) => new ChangelogWindow { Owner = this }.ShowDialog();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
