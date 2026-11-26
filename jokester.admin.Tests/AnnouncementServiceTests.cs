using System.Text.Json;
using jokester.admin.Application.Services;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AnnouncementServiceTests : IDisposable
{
    private readonly SqlSugarClient db = SiteNoticeTestDatabase.Open();
    private readonly SiteNoticeCache cache = new(TimeProvider.System);

    private long Seed(DateTime instant, int status = 1, bool pinned = false, bool deleted = false, string? content = null, string? link = null) =>
        db.Insertable(new SiteAnnouncementEntity
        {
            Title = "标题", Summary = "摘要", Category = "notice", PublishedAt = instant, CreatedAt = instant, Status = status,
            IsPinned = pinned, IsDeleted = deleted, Content = content, LinkUrl = link
        }).ExecuteReturnBigIdentity();

    [Fact]
    public async Task FeedPaginatesPublishedRowsInStableOrderAndKeepsLatestAcrossPages()
    {
        var service = new AnnouncementService(db, cache);
        var now = DateTime.UtcNow.Date;
        var pinned = Seed(now.AddDays(-1), pinned: true);
        var detail = Seed(now, content: "一\n<script>text</script>");
        var link = Seed(now, link: "/pricing");
        var hidden = new[] { Seed(now.AddDays(1), status: 0), Seed(now.AddDays(2), status: 2), Seed(now.AddDays(3), deleted: true) };
        var first = await service.ListAsync(1, 1, default);
        Assert.Equal(3, first.Total);
        Assert.True(first.HasMore);
        Assert.False(Assert.Single(first.Items).HasDetail);
        Assert.Equal(pinned, first.Items[0].Id);
        Assert.Equal(now, first.LatestPublishedAt);
        var second = await service.ListAsync(2, 1, default);
        Assert.True(Assert.Single(second.Items).HasDetail);
        Assert.Equal(link, second.Items[0].Id);
        var third = await service.ListAsync(3, 1, default);
        Assert.False(third.HasMore);
        Assert.Equal(detail, Assert.Single(third.Items).Id);
        var empty = await service.ListAsync(int.MaxValue, 50, default);
        Assert.Empty(empty.Items);
        Assert.Equal(now, empty.LatestPublishedAt);
        var dto = await service.DetailAsync(detail, default);
        Assert.Equal("一\n<script>text</script>", dto.Content);
        Assert.Equal(DateTimeKind.Utc, dto.PublishedAt!.Value.Kind);
        foreach (var id in hidden.Append(999L)) await Assert.ThrowsAsync<NotFoundException>(() => service.DetailAsync(id, default));
        var feedFields = JsonSerializer.SerializeToElement(first.Items[0], SiteNoticeTestDatabase.Json);
        Assert.False(feedFields.TryGetProperty("content", out _));
        foreach (var fields in new[] { feedFields, JsonSerializer.SerializeToElement(dto, SiteNoticeTestDatabase.Json) })
            foreach (var name in new[] { "revision", "status", "createdBy", "updatedBy", "updatedAt" })
                Assert.False(fields.TryGetProperty(name, out _));
    }

    [Fact]
    public async Task EmptyFeedOmitsLatestTimestampAndInvalidPageIsRejected()
    {
        var service = new AnnouncementService(db, cache);
        var feed = await service.ListAsync(1, 20, default);
        Assert.False(JsonSerializer.SerializeToElement(feed, SiteNoticeTestDatabase.Json).TryGetProperty("latestPublishedAt", out _));
        foreach (var (index, size) in new[] { (0, 1), (1, 0), (1, 51) })
            Assert.Equal(400, (await Assert.ThrowsAsync<AppException>(() => service.ListAsync(index, size, default))).Code);
    }

    public void Dispose() => db.Dispose();
}
