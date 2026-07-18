using Launcher.Backend.Models;

namespace Launcher.Backend.Support;

public interface ISupportService
{
    /// <summary>Threads created by the current (anonymous) Supabase identity, newest first.</summary>
    Task<IReadOnlyList<SupportThread>> GetMyThreadsAsync(CancellationToken ct = default);

    Task<SupportThread> CreateThreadAsync(string subject, CancellationToken ct = default);

    /// <summary>Messages in a thread, oldest first.</summary>
    Task<IReadOnlyList<SupportMessage>> GetMessagesAsync(Guid threadId, CancellationToken ct = default);

    Task SendMessageAsync(Guid threadId, string body, CancellationToken ct = default);

    /// <summary>Marks a thread as closed — the player used "Завершить диалог" (or the admin closed it
    /// from Telegram). Closed threads move to "История тем" instead of the active list.</summary>
    Task CloseThreadAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>Permanently deletes a thread and its messages (the FK cascades on delete).</summary>
    Task DeleteThreadAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to new support messages across all of the current user's threads (RLS scopes this
    /// server-side). The callback fires on a background thread; the caller filters by thread if needed.
    /// </summary>
    Task SubscribeToMessagesAsync(Action<SupportMessage> onNewMessage, CancellationToken ct = default);
}
