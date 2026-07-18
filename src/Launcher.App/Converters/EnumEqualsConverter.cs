using System.Globalization;
using System.Windows.Data;

namespace Launcher.App.Converters;

/// <summary>Binds any enum property to a RadioButton: ConverterParameter is the target member's name.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter as string;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}
