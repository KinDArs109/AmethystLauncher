using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.ModSources.Modrinth;

namespace Launcher.App.ViewModels;

public partial class ModSearchItem(ModrinthSearchHit hit) : ObservableObject
{
    public ModrinthSearchHit Hit { get; } = hit;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isInstalling;

    public string AuthorUrl => $"https://modrinth.com/user/{Hit.Author}";

    public string Tags => string.Join(" · ", Hit.DisplayCategories.Take(3));

    public string FormattedDownloads => FormatCount(Hit.Downloads);

    public string FormattedFollows => FormatCount(Hit.Follows);

    public string FormattedDateModified => FormatRelativeDate(Hit.DateModified);

    private static string FormatCount(long count) => count switch
    {
        >= 1_000_000 => $"{count / 1_000_000.0:0.##} млн",
        >= 1_000 => $"{count / 1_000.0:0.#} тыс",
        _ => count.ToString(),
    };

    private static string FormatRelativeDate(DateTimeOffset date)
    {
        var span = DateTimeOffset.UtcNow - date;
        if (span.TotalDays >= 365)
        {
            var years = (int)(span.TotalDays / 365);
            return $"{years} г. назад";
        }

        if (span.TotalDays >= 30)
        {
            var months = (int)(span.TotalDays / 30);
            return $"{months} мес. назад";
        }

        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays} дн. назад";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours} ч. назад";
        }

        return "только что";
    }
}
