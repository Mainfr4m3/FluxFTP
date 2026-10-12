using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal sealed class LayoutSettingsEditor
{
    private readonly Dictionary<string, Editor> _editors = [];
    private sealed record Choice(string Label, string Property) { public override string ToString() => Label; }
    private sealed record Editor(CheckBox[] Columns, ComboBox Sort, ComboBox Direction, ComboBox Font, TextBox Size, CheckBox Bold);

    public LayoutSettingsEditor(Panel host, Dictionary<string, ViewLayoutSettings>? layouts)
    {
        foreach (var (key, title) in new[] { ("Left", "Left file pane"), ("Right", "Right file pane"), ("Queue", "Transfer queue") })
        {
            var settings = layouts?.GetValueOrDefault(key) ?? new(SortBy: key == "Queue" ? "" : "Name");
            var choices = key == "Queue"
                ? new[] { new Choice("Queue order", ""), new("State", "State"), new("Name", "Name"), new("Source", "Source"), new("Destination", "Destination"), new("Progress", "ProgressPercent") }
                : new[] { new Choice("Name", "Name"), new("Size", "SortSize"), new("Modified", "SortModified"), new("Attributes", "Attributes"), new("Status", "Status") };
            var panel = new StackPanel { Margin = new Thickness(12) };
            host.Children.Add(new GroupBox { Header = title, Content = panel, Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(new TextBlock { Text = "Visible columns" });
            var columnsPanel = new WrapPanel();
            panel.Children.Add(columnsPanel);
            var columns = choices.Where(choice => choice.Property != "").Select(choice => new CheckBox
                { Content = choice.Label, IsChecked = settings.HiddenColumns?.Contains(choice.Label) != true, Margin = new Thickness(0, 5, 14, 8) }).ToArray();
            foreach (var column in columns) columnsPanel.Children.Add(column);
            var sort = new ComboBox { ItemsSource = choices, MinWidth = 160 };
            sort.SelectedItem = choices.FirstOrDefault(choice => choice.Property == settings.SortBy) ?? choices[0];
            var direction = new ComboBox { ItemsSource = new[] { "Ascending", "Descending" }, SelectedIndex = settings.Descending ? 1 : 0, MinWidth = 160 };
            var font = new ComboBox { IsEditable = true, ItemsSource = new[] { "Use theme font" }.Concat(Fonts.SystemFontFamilies.Select(family => family.Source).OrderBy(name => name)).ToArray(),
                Text = string.IsNullOrEmpty(settings.FontFamily) ? "Use theme font" : settings.FontFamily, MinWidth = 160 };
            var size = new TextBox { Text = settings.FontSize == 0 ? "" : settings.FontSize.ToString(CultureInfo.CurrentCulture), MinWidth = 70 };
            var bold = new CheckBox { Content = "Bold", IsChecked = settings.Bold };
            void Row(string label, UIElement control)
            {
                var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(control, 1); grid.Children.Add(control); panel.Children.Add(grid);
            }
            Row("Sort by", sort); Row("Sort direction", direction); Row("Font", font); Row("Font size (blank = theme)", size); Row("Font weight", bold);
            _editors[key] = new(columns, sort, direction, font, size, bold);
        }
    }

    public Dictionary<string, ViewLayoutSettings> Read()
    {
        var layouts = new Dictionary<string, ViewLayoutSettings>();
        foreach (var (key, editor) in _editors)
        {
            if (!editor.Columns.Any(column => column.IsChecked == true)) throw new ArgumentException("Keep at least one column visible in each view.");
            double size = 0;
            if (!string.IsNullOrWhiteSpace(editor.Size.Text) && (!double.TryParse(editor.Size.Text, out size) || !double.IsFinite(size) || size is < 8 or > 40))
                throw new ArgumentException("Font size must be between 8 and 40, or blank to use the theme.");
            var font = editor.Font.Text.Trim();
            if (font == "Use theme font") font = "";
            if (font.Length > 0 && !Fonts.SystemFontFamilies.Any(family => family.Source.Equals(font, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Choose an installed font or Use theme font.");
            layouts[key] = new(editor.Columns.Where(column => column.IsChecked != true).Select(column => column.Content.ToString()!).ToArray(),
                ((Choice)editor.Sort.SelectedItem).Property, editor.Direction.SelectedIndex == 1, font, size, editor.Bold.IsChecked == true);
        }
        return layouts;
    }
}
