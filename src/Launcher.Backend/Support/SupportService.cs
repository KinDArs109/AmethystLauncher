using Launcher.Backend.Models;
using Launcher.Backend.Supabase;
using Supabase.Postgrest;
using Supabase.Realtime.PostgresChanges;

namespace Launcher.Backend.Support;

public sealed class SupportService(ISupabaseClientProvider clientProvider) : ISupportService
{
    public async Task<IReadOnlyList<SupportThread>> GetMyThreadsAsync(CancellationToken ct = default)
    {
        var client = await clientProvider.GetClientAsync(ct);
        var result = await client.From<SupportThread>().Get(ct);
        return result.Models.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public async Task<SupportThread> CreateThreadAsync(string subject, CancellationToken ct = default)
    {
        var client = await clientProvider.GetClientAsync(ct);
        var userId = Guid.Parse(client.Auth.CurrentUser!.Id!);

        var thread = new SupportThread { UserId = userId, Subject = subject };
        var result = await client.From<SupportThread>().Insert(thread, cancellationToken: ct);
        return result.Models.Single();
    }

    public async Task<IReadOnlyList<SupportMessage>> GetMessagesAsync(Guid threadId, CancellationToken ct = default)
    {
        var client = await clientProvider.GetClientAsync(ct);
        var result = await client.From<SupportMessage>()
            .Filter("thread_id", Constants.Operator.Equals, threadId.ToString())
            .Order("created_at", Constants.Ordering.Ascending)
            .Get(ct);
        return result.Models;
    }

    public async Task SendMessageAsync(Guid threadId, string body, CancellationToken ct = default)
    {
        var client = await clientProvider.GetClientAsync(ct);
        var message = new SupportMessage { ThreadId = threadId, Sender = "user", Body = body };
        await client.From<SupportMessage>().Insert(message, cancellationToken: ct);
    }

    public async Task CloseThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        var client = await clientProvider.GetClientAsync(ct);
        await client.From<SupportThread>()
            .Filter("id", Constants.Operator.Equals, threadId.ToString())
            .Set(t => t.Status, "closed")
            .Update(cancellationToken: ct);
    }

    public async Task DeleteThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        var client = await clientProvider.GetClientAsync(ct);
        await client.From<SupportThread>()
            .Filter("id", Constants.Operator.Equals, threadId.ToString())
            .Delete(cancellationToken: ct);
    }

    public async Task SubscribeToMessagesAsync(Action<SupportMessage> onNewMessage, CancellationToken ct = default)
    {
        var client = await clientProvider.GetClientAsync(ct);
        await client.From<SupportMessage>().On(PostgresChangesOptions.ListenType.Inserts, (_, change) =>
        {
            var model = change.Model<SupportMessage>();
            if (model is not null)
            {
                onNewMessage(model);
            }
        });
    }
}
