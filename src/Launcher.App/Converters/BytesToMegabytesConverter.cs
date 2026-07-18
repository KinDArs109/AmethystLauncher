using System.Globalization;
using System.Windows.Data;

namespace Launcher.App.Converters;

public sealed class BytesToMegabytesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? bytes / 1048576.0 : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
