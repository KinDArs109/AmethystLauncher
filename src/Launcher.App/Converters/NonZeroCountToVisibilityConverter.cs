using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Launcher.App.Converters;

/// <summary>int count &gt; 0 -> Visible; otherwise Collapsed. Used to hide pagination controls until
/// there's actually a page of results to page through.</summary>
public sealed class NonZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
