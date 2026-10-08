using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

internal static class PathEditorChecks
{
    public static void Run(string themePath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var source = File.OpenRead(themePath);
                var resources = (ResourceDictionary)XamlReader.Load(source);
                var combo = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, Text = "/Plex Media Server", Resources = resources,
                    Style = (Style)resources[typeof(ComboBox)] };
                combo.Measure(new Size(500, 40)); combo.Arrange(new Rect(0, 0, 500, 40)); combo.ApplyTemplate();
                var editor = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
                if (editor.Visibility != Visibility.Visible || editor.Text != combo.Text) throw new Exception("Current path is not displayed.");
                combo.Text = "/Plex Media Server/Crash Reports";
                if (editor.Text != combo.Text) throw new Exception("Navigation does not update path editor.");
                editor.Text = "/TV/My folder";
                if (combo.Text != editor.Text) throw new Exception("Typed path does not update ComboBox.Text.");
                combo.IsEditable = false;
                if (editor.Visibility != Visibility.Collapsed) throw new Exception("Noneditable selectors changed.");
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Path editor regression", failure);
        Console.WriteLine("PASS: themed path editor displays navigation, accepts typed paths with spaces and preserves noneditable selectors.");
    }
}
