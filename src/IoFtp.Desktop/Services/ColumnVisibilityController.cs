using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal sealed class ColumnVisibilityController
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFTP", "columns.json");
    private readonly Dictionary<string, string[]> _hidden;
    private readonly List<Action> _refresh = [];
    private readonly Action<string, string[]> _changed;

    public ColumnVisibilityController(Dictionary<string, ViewLayoutSettings>? layout, Action<string, string[]> changed)
    {
        _changed = changed;
        try { _hidden = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(_path)) ?? []; }
        catch { _hidden = []; }
        if (layout is not null)
            foreach (var (key, value) in layout) _hidden[key] = value.HiddenColumns ?? [];
    }

    public string[] Hidden(string key) => _hidden.GetValueOrDefault(key) ?? [];

    public void Apply(Dictionary<string, ViewLayoutSettings> layout)
    {
        foreach (var (key, value) in layout) _hidden[key] = value.HiddenColumns ?? [];
        foreach (var refresh in _refresh) refresh();
    }

    public void Attach(ListView list, string key, Action resize, Action<string> reportError)
    {
        var view = (GridView)list.View;
        var columns = view.Columns.ToArray();
        // Capture stable labels before sorting adds arrow glyphs to headers.
        var labels = columns.ToDictionary(column => column, column => column.Header is GridViewColumnHeader header
            ? header.Content?.ToString() ?? "Column" : column.Header?.ToString() ?? "Column");
        string Label(GridViewColumn column) => labels[column];
        foreach (var column in columns)
            if (_hidden.GetValueOrDefault(key)?.Contains(Label(column)) == true) view.Columns.Remove(column);
        // Keep a route back to the chooser even when all saved columns were hidden.
        if (view.Columns.Count == 0) view.Columns.Add(columns[0]);

        void Save()
        {
            _hidden[key] = columns.Where(column => !view.Columns.Contains(column)).Select(Label).ToArray();
            try { _changed(key, _hidden[key]); }
            catch (Exception exception) { reportError("Could not save column preferences: " + exception.Message); }
        }

        void Show(GridViewColumn column)
        {
            var index = Array.IndexOf(columns, column);
            var position = view.Columns.Count;
            for (var i = 0; i < view.Columns.Count; i++)
                if (Array.IndexOf(columns, view.Columns[i]) > index) { position = i; break; }
            view.Columns.Insert(position, column);
        }

        void Populate(ItemCollection items)
        {
            items.Clear();
            foreach (var column in columns)
            {
                var visible = view.Columns.Contains(column);
                var item = new MenuItem { Header = Label(column), IsCheckable = true, IsChecked = visible,
                    StaysOpenOnClick = true, IsEnabled = !visible || view.Columns.Count > 1 };
                item.Click += (_, _) =>
                {
                    if (item.IsChecked) Show(column);
                    else view.Columns.Remove(column);
                    resize();
                    Save();
                    foreach (MenuItem choice in items.OfType<MenuItem>().Where(choice => choice.IsCheckable))
                        choice.IsEnabled = !choice.IsChecked || view.Columns.Count > 1;
                };
                items.Add(item);
            }
            items.Add(new Separator());
            var reset = new MenuItem { Header = "Show all columns" };
            reset.Click += (_, _) =>
            {
                foreach (var column in columns.Where(column => !view.Columns.Contains(column))) Show(column);
                resize();
                Save();
            };
            items.Add(reset);
        }

        var headerMenu = new ContextMenu();
        headerMenu.Opened += (_, _) => Populate(headerMenu.Items);
        view.ColumnHeaderContextMenu = headerMenu;
        var chooser = new MenuItem { Header = "Columns" };
        chooser.SubmenuOpened += (_, _) => Populate(chooser.Items);
        chooser.Items.Add(new MenuItem { Header = "Show columns" });
        list.ContextMenu ??= new ContextMenu();
        list.ContextMenu.Items.Add(new Separator());
        list.ContextMenu.Items.Add(chooser);
        _refresh.Add(() =>
        {
            foreach (var column in columns)
            {
                var visible = !Hidden(key).Contains(Label(column));
                if (visible && !view.Columns.Contains(column)) Show(column);
                else if (!visible && view.Columns.Contains(column)) view.Columns.Remove(column);
            }
            if (view.Columns.Count == 0) view.Columns.Add(columns[0]);
            resize();
        });
        resize();
    }
}
