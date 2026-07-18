using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Launcher.Backend.Models;

[Table("announcements")]
public sealed class Announcement : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("body")]
    public string Body { get; set; } = "";

    [Column("author")]
    public string? Author { get; set; }

    [Column("severity")]
    public string Severity { get; set; } = "info";

    [Column("pinned")]
    public bool Pinned { get; set; }

    [Column("created_at", ignoreOnInsert: true)]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }
}
