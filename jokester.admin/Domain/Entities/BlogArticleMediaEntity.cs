using SqlSugar;

namespace jokester.admin.Domain.Entities;

[SugarTable("blog_article_media")]
public sealed class BlogArticleMediaEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id")]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "article_id")]
    public long ArticleId { get; set; }

    [SugarColumn(ColumnName = "media_id")]
    public long MediaId { get; set; }

    [SugarColumn(ColumnName = "created_at")]
    public DateTime CreatedAt { get; set; }
}
