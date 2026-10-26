using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace IoFtp.Linux;
internal sealed class SectionsWindow : Window
{
    public SectionsWindow(LinuxConfiguration configuration, Action changed)
    {
        Title = "Sections"; Width = 820; Height = 590; MinWidth = 620; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), Margin = new Thickness(16) };
        var names = new ListBox(); var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var newButton = new Button { Content = "New section", Padding = new Thickness(10, 6) };
        DockPanel.SetDock(newButton, Dock.Bottom); left.Children.Add(newButton); left.Children.Add(names); root.Children.Add(left);
        var right = new DockPanel(); Grid.SetColumn(right, 1); root.Children.Add(right);
        var form = SettingsUi.Form(); form.Margin = new Thickness(0); var editor = new ScrollViewer { Content = form }; right.Children.Add(editor);
        var report = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FFB179") };
        string? original = null; TextBox sectionName = null!; Dictionary<string, TextBox> paths = [];
        void Reload(string? select = null)
        {
            var sections = configuration.Load().Sections.OrderBy(section => section.Name).ToList(); names.ItemsSource = sections;
            names.SelectedItem = sections.FirstOrDefault(section => section.Name.Equals(select, StringComparison.OrdinalIgnoreCase));
        }
        void Edit(LinuxSection? section)
        {
            original = section?.Name; form.Children.Clear(); paths.Clear();
            sectionName = SettingsUi.Field(form, "Section name", section?.Name ?? "");
            SettingsUi.Note(form, "Enter the remote path for each site. Leave a site blank to exclude it from this section.");
            foreach (var site in configuration.Load().Sites.OrderBy(site => site.Profile.Name))
            {
                var value = section?.SitePaths.FirstOrDefault(pair => pair.Key.Equals(site.Profile.Name, StringComparison.OrdinalIgnoreCase)).Value ?? "";
                paths[site.Profile.Name] = SettingsUi.Field(form, site.Profile.Name, value);
            }
            var actions = new WrapPanel();
            actions.Children.Add(Ui.Button("Save section", () =>
            {
                try
                {
                    var name = sectionName.Text?.Trim() ?? "";
                    LinuxSettings.SaveSection(configuration, original, name, paths.Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Text)).ToDictionary(pair => pair.Key, pair => pair.Value.Text!));
                    changed(); Reload(name); report.Text = "Section saved.";
                }
                catch (Exception ex) { report.Text = ex.Message; }
                return Task.CompletedTask;
            }));
            actions.Children.Add(Ui.Button("Delete section", async () =>
            {
                if (original is null) return;
                if (!await SettingsUi.Confirm(this, "Delete section", "Delete section " + original + "? Remote files will remain on the servers.")) return;
                configuration.Update(state => state with { Sections = state.Sections.Where(saved => !saved.Name.Equals(original, StringComparison.OrdinalIgnoreCase)).ToList() });
                changed(); Reload(); Edit(null); report.Text = "Section deleted.";
            }));
            form.Children.Add(actions); form.Children.Add(report);
        }
        newButton.Click += (_, _) => { names.SelectedItem = null; Edit(null); };
        names.SelectionChanged += (_, _) => { if (names.SelectedItem is LinuxSection section) Edit(section); };
        Reload(); Edit(null); Content = root;
    }
}
