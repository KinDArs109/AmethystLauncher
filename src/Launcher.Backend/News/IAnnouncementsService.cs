using Launcher.Backend.Models;

namespace Launcher.Backend.News;

public interface IAnnouncementsService
{
    /// <summary>Published announcements, pinned first, newest first.</summary>
    Task<IReadOnlyList<Announcement>> GetPublishedAsync(CancellationToken ct = default);

    /// <summary>
    /// Subscribes to live inserts/updates on the announcements table. The callback fires on a background
    /// thread for every change — including drafts — the caller is responsible for filtering/re-sorting.
    /// </summary>
    Task SubscribeAsync(Action<Announcement> onChange, CancellationToken ct = default);
}
