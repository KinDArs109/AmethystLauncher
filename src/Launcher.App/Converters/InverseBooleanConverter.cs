using System.Globalization;
using System.Windows.Data;

namespace Launcher.App.Converters;

/// <summary>Plain bool negation for two-way bindings (e.g. mutually exclusive RadioButtons).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}
