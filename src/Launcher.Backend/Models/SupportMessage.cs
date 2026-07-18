using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Launcher.Backend.Models;

[Table("support_messages")]
public sealed class SupportMessage : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("thread_id")]
    public Guid ThreadId { get; set; }

    [Column("sender")]
    public string Sender { get; set; } = "user";

    [Column("body")]
    public string Body { get; set; } = "";

    [Column("created_at", ignoreOnInsert: true)]
    public DateTimeOffset CreatedAt { get; set; }
}
