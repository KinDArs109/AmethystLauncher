using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Launcher.Loaders.Abstractions;

namespace Launcher.App.Converters;

/// <summary>ModLoaderType.Vanilla -> Collapsed, any actual loader -> Visible.</summary>
public sealed class ModLoaderTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ModLoaderType.Vanilla ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
