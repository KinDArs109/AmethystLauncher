using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Launcher.Backend.Models;

[Table("support_threads")]
public sealed class SupportThread : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("subject")]
    public string Subject { get; set; } = "";

    [Column("status")]
    public string Status { get; set; } = "open";

    [Column("created_at", ignoreOnInsert: true)]
    public DateTimeOffset CreatedAt { get; set; }
}
