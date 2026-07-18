using System.Globalization;
using System.Windows.Data;
using Launcher.App.ViewModels;

namespace Launcher.App.Converters;

/// <summary>
/// Binds a <see cref="GameLogLevel"/>? filter property to a group of RadioButtons, one per level plus "All".
/// ConverterParameter is the level name ("All", "Error", "Warn", "Info").
/// </summary>
public sealed class LogFilterRadioConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var current = value as GameLogLevel?;
        var target = parameter as string;
        return target == "All" ? current is null : current?.ToString() == target;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
        {
            return Binding.DoNothing;
        }

        var target = parameter as string;
        return target == "All" || target is null ? null : Enum.Parse<GameLogLevel>(target);
    }
}
