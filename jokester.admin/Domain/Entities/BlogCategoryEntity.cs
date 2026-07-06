using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("blog_category")]
public sealed class BlogCategoryEntity
{
    public long Id { get; set; }

    public long SiteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Sort { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public long? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
}
