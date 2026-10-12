using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal sealed class SkippedEntryColorConverter(Func<GlobalSettings> settings, Func<object> normalColor) : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var current = settings();
        var name = values.ElementAtOrDefault(0) as string ?? "";
        var directory = values.ElementAtOrDefault(1) is true;
        long? size = values.ElementAtOrDefault(2) is long bytes ? bytes : null;
        // Preview either enabled transfer mode in the browser; actual transfer
        // filtering still distinguishes manual and automated requests.
        var skipped = SkipRuleMatcher.ShouldSkip(current, name, directory, "Transfer", false, size) ||
            SkipRuleMatcher.ShouldSkip(current, name, directory, "Transfer", true, size);
        if (!skipped) return normalColor();
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(current.SkippedItemColor)); }
        catch { return normalColor(); }
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => DependencyProperty.UnsetValue).ToArray();
}
