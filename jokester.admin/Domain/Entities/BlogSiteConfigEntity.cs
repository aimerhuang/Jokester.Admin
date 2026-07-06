using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("blog_site_config")]
public sealed class BlogSiteConfigEntity
{
    public long Id { get; set; }

    public long SiteId { get; set; }

    public DateTime BuildDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}
