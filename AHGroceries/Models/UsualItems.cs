using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AHGroceries.Models;

[Table("usual_items")]
public class UsualItem : BaseModel
{
    [PrimaryKey("id", shouldInsert: false)]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("category")]
    public string Category { get; set; } = "General";

    [Column("is_hidden")]
    public bool IsHidden { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }
}