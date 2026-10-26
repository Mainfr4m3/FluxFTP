using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Media;
using System.Runtime.InteropServices;
using IoFtp.Core.Transport;
namespace IoFtp.Linux;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (OperatingSystem.IsLinux()) NativeLibrary.SetDllImportResolver(typeof(FtpRemoteSession).Assembly,
            (name, assembly, path) => name == "libssl-3-x64" ? NativeLibrary.Load("libssl.so.3") : name == "libcrypto-3-x64" ? NativeLibrary.Load("libcrypto.so.3") : IntPtr.Zero);
        if (args.FirstOrDefault() == "--backup-checks") return BackupImportChecks.Run(args[1]);
        if (args.FirstOrDefault() is "--smoke-test" or "--ftp-check") return SmokeTests.Run(args).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--api-check")
            return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).StartWithClassicDesktopLifetime(args);
        if (args.FirstOrDefault() == "--settings-checks")
        {
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
            return SettingsChecks.Run();
        }
        if (args.FirstOrDefault() == "--ui-check")
        {
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
            var window = new MainWindow(); window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Console.WriteLine("PASS: Linux UI created: " + window.Title); window.Close(); return 0;
        }
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme());
        Styles.Add(new Style(selector => selector.OfType<Window>()) { Setters = { new Setter(Window.BackgroundProperty, Brush.Parse("#101820")), new Setter(Window.ForegroundProperty, Brush.Parse("#E5EEF4")), new Setter(Window.FontSizeProperty, 13d) } });
        Styles.Add(new Style(selector => selector.OfType<Button>()) { Setters = { new Setter(Button.BackgroundProperty, Brush.Parse("#20313F")), new Setter(Button.ForegroundProperty, Brush.Parse("#E5EEF4")), new Setter(Button.BorderBrushProperty, Brush.Parse("#354C5D")), new Setter(Button.BorderThicknessProperty, new Thickness(1)), new Setter(Button.VerticalAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center) } });
        Styles.Add(new Style(selector => selector.OfType<TextBox>()) { Setters = { new Setter(TextBox.BackgroundProperty, Brush.Parse("#0D171F")), new Setter(TextBox.BorderBrushProperty, Brush.Parse("#354C5D")) } });
        Styles.Add(new Style(selector => selector.OfType<ListBox>()) { Setters = { new Setter(ListBox.BackgroundProperty, Brush.Parse("#17232D")), new Setter(ListBox.BorderBrushProperty, Brush.Parse("#354C5D")), new Setter(ListBox.BorderThicknessProperty, new Thickness(1)) } });
        Styles.Add(new Style(selector => selector.OfType<ListBoxItem>()) { Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(7, 3)), new Setter(ListBoxItem.MinHeightProperty, 26d) } });
        Styles.Add(new Style(selector => selector.OfType<TabItem>()) { Setters = { new Setter(TabItem.FontSizeProperty, 13d), new Setter(TabItem.PaddingProperty, new Thickness(10, 8)) } });
        Styles.Add(new Style(selector => selector.OfType<ComboBox>()) { Setters = { new Setter(ComboBox.BackgroundProperty, Brush.Parse("#17232D")), new Setter(ComboBox.BorderBrushProperty, Brush.Parse("#354C5D")) } });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(); desktop.MainWindow = window;
            if (desktop.Args?.FirstOrDefault() == "--api-check")
                window.Opened += (_, _) => Avalonia.Threading.DispatcherTimer.RunOnce(async () =>
                {
                    var exit = 0;
                    try { await ApiIntegrationChecks.Run(window, desktop.Args); }
                    catch (Exception ex) { Console.Error.WriteLine(ex); exit = 1; }
                    window.Close(); desktop.Shutdown(exit);
                }, TimeSpan.FromMilliseconds(300));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
