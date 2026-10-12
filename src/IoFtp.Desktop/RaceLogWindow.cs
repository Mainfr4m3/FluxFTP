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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CheckBox _follow = new() { Content = "Follow live", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 5, 5, 5) };
    private bool _updating;
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
        bar.Children.Add(_files); bar.Children.Add(refresh); bar.Children.Add(_follow);
        _follow.Checked += (_, _) => FollowLatest();
        _text.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            // Content/layout updates must not be mistaken for manual scrolling.
            if (_updating || e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0 || e.VerticalChange == 0) return;
            _follow.IsChecked = e.VerticalOffset >= Math.Max(0, e.ExtentHeight - e.ViewportHeight - 2);
        }));
        DockPanel.SetDock(bar, Dock.Top); panel.Children.Add(bar);
        DockPanel.SetDock(_status, Dock.Bottom); panel.Children.Add(_status);
        panel.Children.Add(_text); Content = panel;
        _files.SelectionChanged += (_, _) =>
        {
            _follow.IsChecked = string.Equals(_files.SelectedItem as string, _store.ActivePath, StringComparison.OrdinalIgnoreCase);
            Read();
        };
        _timer.Tick += (_, _) => Refresh();
        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) => FollowLatest();
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
            if (_text.Text != value)
            {
                var offset = _text.VerticalOffset;
                var selection = _text.SelectionStart;
                var selectionLength = _text.SelectionLength;
                _updating = true;
                try
                {
                    if (value.StartsWith(_text.Text, StringComparison.Ordinal)) _text.AppendText(value[_text.Text.Length..]);
                    else _text.Text = value;
                    _text.Select(Math.Min(selection, value.Length), Math.Min(selectionLength, Math.Max(0, value.Length - selection)));
                    _text.UpdateLayout();
                    if (_follow.IsChecked == true) _text.ScrollToEnd();
                    else _text.ScrollToVerticalOffset(offset);
                }
                finally { _updating = false; }
                FollowLatest();
            }
            _status.Text = _store.LastError ?? (_follow.IsChecked == true ? "Live — following latest events." : "Live updates continue; following paused. Enable Follow live to jump to the latest event.") + " Logs rotate every 24 hours.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = "Log unavailable; refresh to try again."; }
    }

    private void FollowLatest()
    {
        // Scroll after WPF measures the appended text, including initial load.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_follow.IsChecked != true || !IsVisible) return;
            _updating = true;
            try { _text.ScrollToEnd(); }
            finally { _updating = false; }
        }));
    }
}
