using Launcher.Backend.Models;
using Launcher.Backend.Supabase;
using Supabase.Realtime.PostgresChanges;

namespace Launcher.Backend.News;

public sealed class AnnouncementsService(ISupabaseClientProvider clientProvider) : IAnnouncementsService
{
    public async Task<IReadOnlyList<Announcement>> GetPublishedAsync(CancellationToken ct = default)
    {
        var client = await clientProvider.GetClientAsync(ct);
        var result = await client.From<Announcement>().Get(ct);

        return result.Models
            .Where(a => a.PublishedAt is not null)
            .OrderByDescending(a => a.Pinned)
            .ThenByDescending(a => a.CreatedAt)
            .ToList();
    }

    public async Task SubscribeAsync(Action<Announcement> onChange, CancellationToken ct = default)
    {
        var client = await clientProvider.GetClientAsync(ct);
        await client.From<Announcement>().On(PostgresChangesOptions.ListenType.All, (_, change) =>
        {
            var model = change.Model<Announcement>();
            if (model is not null)
            {
                onChange(model);
            }
        });
    }
}
