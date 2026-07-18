using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Launcher.App.ViewModels;

namespace Launcher.App.Converters;

public sealed class OtherLoaderVersionModeVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LoaderVersionMode.Other ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
