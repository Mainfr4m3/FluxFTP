using System.Windows;
using IoFtp.Desktop.Services;
namespace IoFtp.Desktop;
internal sealed class RaceLogWindow : Window
{
    public RaceLogWindow(RaceLogStore store, string label = "RACE-Log")
    {
        Title = "FluxFTP — " + label; Width = 1050; Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        Content = new LogViewerControl(store, label);
    }
}
