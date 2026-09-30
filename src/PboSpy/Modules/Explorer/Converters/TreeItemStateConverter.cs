using PboSpy.Interfaces;
using System.Globalization;
using System.Windows.Data;

namespace PboSpy.Modules.Explorer.Converters;

/// <summary>values: [0] tree item, [1] selection host, [2..] change counters that only exist to re-trigger.</summary>
internal class TreeItemSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length > 1 && values[0] is ITreeItem item && values[1] is ITreeSelectionHost host && host.IsSelected(item);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal class TreeItemSelectableConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length < 2 || values[0] is not ITreeItem item || values[1] is not ITreeSelectionHost host || host.IsSelectable(item);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal class TreeItemVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 1 && values[0] is ITreeItem item && values[1] is ViewModels.ExplorerViewModel host &&
            (host.HideFilteredFiles && !host.IsSelectable(item) || host.IsHiddenBySearch(item)))
        {
            return System.Windows.Visibility.Collapsed;
        }
        return System.Windows.Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
