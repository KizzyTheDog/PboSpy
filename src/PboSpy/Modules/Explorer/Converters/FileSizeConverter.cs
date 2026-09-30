using PboSpy.Models;
using PboSpy.Services;
using System.Globalization;
using System.Windows.Data;

namespace PboSpy.Modules.Explorer.Converters;

internal class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!AppSettings.Default.ShowFileSizes || value is not FileBase file)
        {
            return "";
        }
        try
        {
            return Format(file.DataSize);
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
