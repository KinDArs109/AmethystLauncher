using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Launcher.App.ViewModels;

namespace Launcher.App.Converters;

/// <summary>Binds SelectedTab to per-tab panel visibility: ConverterParameter is the target InstanceDetailTab name.</summary>
public sealed class InstanceDetailTabVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is InstanceDetailTab tab && parameter is string name && tab.ToString() == name
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
