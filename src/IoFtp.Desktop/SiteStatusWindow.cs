using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace IoFtp.Desktop;

internal sealed class SiteStatusPanel : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    internal SiteStatusPanel(Func<IReadOnlyList<(Guid Id, string Name)>> sites, Func<Guid, string> details, Func<Task>? checkHealth = null)
    {
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new DockPanel { Margin = new Thickness(12) };
        if (checkHealth is not null)
        {
            var check = new Button { Content = "Check all sites now", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            check.Click += async (_, _) => { check.IsEnabled = false; try { await checkHealth(); } finally { check.IsEnabled = true; } };
            DockPanel.SetDock(check, Dock.Top); panel.Children.Add(check);
        }
        var select = new ComboBox { DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 0, 10) };
        var choices = sites().Select(site => new SiteChoice(site.Id, site.Name)).ToArray();
        select.ItemsSource = choices;
        DockPanel.SetDock(select, Dock.Top); panel.Children.Add(select);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        text.SetResourceReference(FontFamilyProperty, "AppMonospaceFontFamily");
        panel.Children.Add(text); Content = panel;
        void Refresh() { text.Text = select.SelectedItem is SiteChoice site ? details(site.Id) : "No configured sites."; }
        select.SelectionChanged += (_, _) => Refresh();
        select.SelectedIndex = choices.Length > 0 ? 0 : -1;
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _timer.Start(); } else _timer.Stop(); };
    }
    private sealed record SiteChoice(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }
}
