using System.Windows;
using System.Windows.Controls;

namespace IoFtp.Desktop;

internal static class ImportPasswordPrompt
{
    public static bool Show(Window owner)
    {
        var window = new Window { Owner = owner, Title = "Import site passwords", Width = 530,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        window.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(22) };
        var title = new TextBlock { Text = "IMPORT PASSWORDS", FontSize = 20, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = "Some sites have no imported password. Choose an optional UTF-8 text file with one entry per line:\n\nftp://user:password@host:port\nor Site name=password\n\nYou can also select the file from the import window.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 18) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var skip = new Button { Content = "Skip", IsCancel = true };
        var choose = new Button { Content = "Choose text file...", IsDefault = true };
        choose.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(skip); buttons.Children.Add(choose); panel.Children.Add(buttons);
        window.Content = panel;
        return window.ShowDialog() == true;
    }
}
