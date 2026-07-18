namespace Launcher.App.Services;

/// <summary>
/// Picks the correct Russian plural form for a count: 1 элемент / 2 элемента / 5 элементов.
/// </summary>
public static class RussianPlural
{
    public static string Of(int count, string one, string few, string many)
    {
        var mod100 = Math.Abs(count) % 100;
        if (mod100 is >= 11 and <= 14)
        {
            return many;
        }

        return (mod100 % 10) switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }
}
