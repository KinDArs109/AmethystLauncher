using System.Globalization;
using System.Windows.Data;

namespace Launcher.App.Converters;

/// <summary>Non-null, non-whitespace string -> true; null/empty -> false. Used to drive triggers
/// (e.g. a floating input label) that need a bool rather than a Visibility.</summary>
public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrEmpty(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
