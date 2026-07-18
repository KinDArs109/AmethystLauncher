using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Launcher.App.Converters;

/// <summary>Opposite of the stock BooleanToVisibilityConverter: true -> Collapsed, false -> Visible.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
