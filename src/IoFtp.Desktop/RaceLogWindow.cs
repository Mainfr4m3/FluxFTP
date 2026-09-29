using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IoFtp.Desktop.Services;

namespace IoFtp.Desktop;

internal sealed class RaceLogWindow : Window
{
    private readonly RaceLogStore _store;
    private readonly ComboBox _files = new() { MinWidth = 300, Margin = new Thickness(5) };
    private readonly TextBox _text = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    public RaceLogWindow(RaceLogStore store)
    {
        _store = store; Title = "FluxFTP — RACE-Log"; Width = 1050; Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        _files.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        var panel = new DockPanel { Margin = new Thickness(10) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(5) };
        refresh.Click += (_, _) => Refresh();
        bar.Children.Add(_files); bar.Children.Add(refresh);
        DockPanel.SetDock(bar, Dock.Top); panel.Children.Add(bar);
        DockPanel.SetDock(_status, Dock.Bottom); panel.Children.Add(_status);
        panel.Children.Add(_text); Content = panel;
        _files.SelectionChanged += (_, _) => Read();
        _timer.Tick += (_, _) => Refresh();
        Closed += (_, _) => _timer.Stop();
        Refresh(); _timer.Start();
    }
    private void Refresh()
    {
        try
        {
            var selected = _files.SelectedItem as string;
            var paths = new[] { _store.ActivePath }.Concat(Directory.Exists(_store.ArchiveDirectory)
                ? Directory.GetFiles(_store.ArchiveDirectory, "RACE-*.log").OrderByDescending(p => p)
                : Enumerable.Empty<string>()).ToArray();
            if (!paths.SequenceEqual(_files.Items.Cast<string>()))
            { _files.ItemsSource = paths; _files.SelectedItem = paths.Contains(selected) ? selected : paths[0]; }
            Read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = "Unable to read the RACE log folder."; }
    }
    private void Read()
    {
        if (_files.SelectedItem is not string path) return;
        try
        {
            var value = _store.ReadTail(path);
            if (_text.Text != value) { _text.Text = value; _text.ScrollToEnd(); }
            _status.Text = _store.LastError ?? "Rotates every 24 hours. Older logs are kept in the logs subfolder.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = "Log unavailable; refresh to try again."; }
    }
}
