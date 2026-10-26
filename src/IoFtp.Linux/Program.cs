using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
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
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
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
