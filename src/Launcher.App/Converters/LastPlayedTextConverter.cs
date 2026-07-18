using System.Globalization;
using System.Windows.Data;

namespace Launcher.App.Converters;

public sealed class LastPlayedTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset lastPlayed)
        {
            return "Ещё не запускалась";
        }

        var delta = DateTimeOffset.UtcNow - lastPlayed;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "Играли только что";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"Играли {(int)delta.TotalMinutes} мин назад";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"Играли {(int)delta.TotalHours} ч назад";
        }

        if (delta < TimeSpan.FromDays(30))
        {
            return $"Играли {(int)delta.TotalDays} дн назад";
        }

        return $"Играли {lastPlayed.LocalDateTime:dd.MM.yyyy}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
