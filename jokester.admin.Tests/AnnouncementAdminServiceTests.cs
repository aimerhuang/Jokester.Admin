using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Announcements;
using jokester.admin.Application.Services;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AnnouncementAdminServiceTests : IDisposable
{
    private readonly SqlSugarClient db = SiteNoticeTestDatabase.Open();
    private readonly SiteNoticeCache cache = new(TimeProvider.System);
    private readonly Mock<ICurrentUser> user = new();
    private readonly string webRoot = Path.Combine(Path.GetTempPath(), $"announcement-cover-test-{Guid.NewGuid():N}");
    private readonly AnnouncementAdminService admin;
    private readonly AnnouncementService feed;
    private readonly long operatorId;

    public AnnouncementAdminServiceTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.WebRootPath).Returns(webRoot);
        operatorId = db.Insertable(new SysUserEntity
        {
            UserName = "editor", IsSuperAdmin = true, Status = 1, CreatedAt = DateTime.UtcNow
        }).ExecuteReturnBigIdentity();
        user.SetupGet(x => x.UserId).Returns(operatorId);
        user.SetupGet(x => x.IsSuperAdmin).Returns(true);
        admin = new(db, user.Object, env.Object, cache);
        feed = new(db, cache);
    }

    private static CreateAnnouncementRequest Create(int status = 0, bool pinned = false) => new()
        { Title = " 首条公告 ", Summary = "说明", Category = "notice", Status = status, IsPinned = pinned };
    private static UpdateAnnouncementRequest Update(AdminAnnouncementDetail row) => new()
        { Title = row.Title, Summary = row.Summary, Category = row.Category, Content = row.Content, CoverUrl = row.CoverUrl,
            IsPinned = row.IsPinned, IsImportant = row.IsImportant, LinkUrl = row.LinkUrl, LinkText = row.LinkText, Revision = row.Revision };
    private static async Task Error(string machineCode, Func<Task> work) =>
        Assert.Equal(machineCode, (await Assert.ThrowsAnyAsync<AppException>(work)).MachineCode);

    [Fact]
    public async Task LifecyclePreservesFirstPublicationOptimisticRevisionAndFeedInvalidation()
    {
        var draft = await admin.CreateAsync(Create(), default);
        Assert.Equal("首条公告", draft.Title);
        Assert.Null(draft.PublishedAt);
        Assert.Equal("editor", draft.CreatedByName);
        Assert.Equal(operatorId, draft.UpdatedBy);
        Assert.Empty((await feed.ListAsync(1, 20, default)).Items);
        await Error("VALIDATION_ERROR", () => admin.ChangeStatusAsync(draft.Id, new(2, draft.Revision), default));
        await Error("VALIDATION_ERROR", () => admin.ChangeStatusAsync(draft.Id, new(1, null), default));
        var published = await admin.ChangeStatusAsync(draft.Id, new(1, draft.Revision), default);
        var firstAt = Assert.Single((await feed.ListAsync(1, 20, default)).Items).PublishedAt;
        Assert.NotNull(firstAt);
        Assert.Equal(draft.Revision + 1, published.Revision);
        var same = await admin.ChangeStatusAsync(draft.Id, new(1, published.Revision), default);
        Assert.Equal(published.Revision, same.Revision);
        await Error("CONFLICT", () => admin.DeleteAsync(draft.Id, same.Revision, default));
        await Error("ANNOUNCEMENT_REVISION_CONFLICT", () => admin.UpdateAsync(draft.Id, Update(draft), default));
        await Error("VALIDATION_ERROR", () => admin.ChangeStatusAsync(draft.Id, new(0, same.Revision), default));
        var edited = await admin.UpdateAsync(draft.Id, Update(published) with { Title = "修改", Content = "第一段\r\n第二段" }, default);
        Assert.Equal(firstAt, edited.PublishedAt);
        Assert.Equal("第一段\n第二段", (await feed.DetailAsync(draft.Id, default)).Content);
        var offline = await admin.ChangeStatusAsync(draft.Id, new(2, edited.Revision), default);
        Assert.Empty((await feed.ListAsync(1, 20, default)).Items);
        await Error("VALIDATION_ERROR", () => admin.ChangeStatusAsync(draft.Id, new(0, offline.Revision), default));
        var republished = await admin.ChangeStatusAsync(draft.Id, new(1, offline.Revision), default);
        Assert.Equal(firstAt, republished.PublishedAt);
        var final = await admin.ChangeStatusAsync(draft.Id, new(2, republished.Revision), default);
        await Error("ANNOUNCEMENT_REVISION_CONFLICT", () => admin.DeleteAsync(draft.Id, offline.Revision, default));
        await admin.DeleteAsync(draft.Id, final.Revision, default);
        await Error("RESOURCE_NOT_FOUND", () => admin.DetailAsync(draft.Id, default));
        Assert.Empty((await admin.ListAsync(new(), default)).Items);
        Assert.True(db.Queryable<SiteAnnouncementEntity>().Single().IsDeleted);
    }

    [Fact]
    public async Task CanDeleteDraftAndLimitsPinsOnCreateStatusAndContentChanges()
    {
        var first = await admin.CreateAsync(Create(1, true), default);
        await admin.CreateAsync(Create(1, true), default);
        await admin.CreateAsync(Create(1, true), default);
        await Error("CONFLICT", () => admin.CreateAsync(Create(1, true), default));
        var draft = await admin.CreateAsync(Create(0, true), default);
        await Error("CONFLICT", () => admin.ChangeStatusAsync(draft.Id, new(1, draft.Revision), default));
        var fourth = await admin.CreateAsync(Create(1, false), default);
        await Error("CONFLICT", () => admin.UpdateAsync(fourth.Id, Update(fourth) with { IsPinned = true }, default));
        await admin.UpdateAsync(first.Id, Update(first) with { IsPinned = false }, default);
        var pinned = await admin.UpdateAsync(fourth.Id, Update(fourth) with { IsPinned = true }, default);
        Assert.True(pinned.IsPinned);
        await admin.DeleteAsync(draft.Id, draft.Revision, default);
        Assert.Equal(3, (await feed.ListAsync(1, 20, default)).Items.Count(x => x.IsPinned));
    }

    [Fact]
    public async Task ListsByLiteralKeywordAndStatusAndDisplaysLastEditor()
    {
        await admin.CreateAsync(Create() with { Title = "50% 进度" }, default);
        await admin.CreateAsync(Create(1), default);
        var list = await admin.ListAsync(new() { Keyword = "%", Status = 0 }, default);
        var result = Assert.Single(list.Items);
        Assert.Equal(1, list.Total);
        Assert.Equal("50% 进度", result.Title);
        Assert.Equal("editor", result.UpdatedByName);
        Assert.Equal(DateTimeKind.Utc, result.UpdatedAt!.Value.Kind);
        Assert.Empty((await admin.ListAsync(new() { Keyword = "%", Status = 1 }, default)).Items);
    }

    [Fact]
    public async Task NonAdminCannotReadOrWrite()
    {
        user.SetupGet(x => x.IsSuperAdmin).Returns(false);
        await Error("RESOURCE_FORBIDDEN", () => admin.ListAsync(new(), default));
        await Error("RESOURCE_FORBIDDEN", () => admin.DetailAsync(1, default));
        await Error("RESOURCE_FORBIDDEN", () => admin.CreateAsync(Create(), default));
    }

    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, true)]
    public async Task WritesAndUploadRecheckOperatorInDatabase(bool super, int status, bool deleted)
    {
        var draft = await admin.CreateAsync(Create(), default);
        db.Updateable<SysUserEntity>().SetColumns(x => new SysUserEntity { IsSuperAdmin = super, Status = status, IsDeleted = deleted })
            .Where(x => x.Id == operatorId).ExecuteCommand();
        await Error("RESOURCE_FORBIDDEN", () => admin.UpdateAsync(draft.Id, Update(draft), default));
        await Error("RESOURCE_FORBIDDEN", () => admin.ChangeStatusAsync(draft.Id, new(1, draft.Revision), default));
        await Error("RESOURCE_FORBIDDEN", () => admin.DeleteAsync(draft.Id, draft.Revision, default));
        await Error("RESOURCE_FORBIDDEN", () => admin.UploadCoverAsync(FileFor([]), default));
        Assert.Equal(draft.Revision, (await admin.DetailAsync(draft.Id, default)).Revision);
    }

    [Fact]
    public async Task InvalidAnnouncementsDoNotPersist()
    {
        var request = Create();
        foreach (var invalid in new[] { request with { Category = "other" }, request with { Status = 2 }, request with { Title = " " },
            request with { CoverUrl = "/site-media/announcement/202611/00000000000000000000000000000000.png" },
            request with { CoverUrl = "https://example.com/a.png" }, request with { LinkText = "Go" }, request with { LinkUrl = "/pricing" } })
            await Error("VALIDATION_ERROR", () => admin.CreateAsync(invalid, default));
        Assert.Equal(0, db.Queryable<SiteAnnouncementEntity>().Count());
    }

    private static FormFile FileFor(byte[] bytes) => new(new MemoryStream(bytes), 0, bytes.Length, "file", "cover.png");

    [Fact]
    public async Task UploadRejectsFakeOversizeAndGifAndStoresDecodableCoverForPublication()
    {
        await Error("VALIDATION_ERROR", () => admin.UploadCoverAsync(FileFor(Encoding.UTF8.GetBytes("invalid image")), default));
        await Error("VALIDATION_ERROR", () => admin.UploadCoverAsync(FileFor(new byte[2 * 1024 * 1024 + 1]), default));
        using var image = new Image<Rgba32>(3, 2);
        using var gif = new MemoryStream();
        await image.SaveAsGifAsync(gif);
        await Error("VALIDATION_ERROR", () => admin.UploadCoverAsync(FileFor(gif.ToArray()), default));
        using var png = new MemoryStream();
        await image.SaveAsPngAsync(png);
        var uploaded = await admin.UploadCoverAsync(FileFor(png.ToArray()), default);
        Assert.Matches(@"^/site-media/announcement/\d{6}/[0-9a-f]{32}\.png$", uploaded.Url);
        using var stored = await Image.LoadAsync(Path.Combine(webRoot, uploaded.Url[1..]));
        Assert.Equal(3, stored.Width);
        var created = await admin.CreateAsync(Create(1) with { CoverUrl = uploaded.Url }, default);
        Assert.Equal(uploaded.Url, (await feed.DetailAsync(created.Id, default)).CoverUrl);
    }

    public void Dispose()
    {
        db.Dispose();
        if (Directory.Exists(webRoot)) Directory.Delete(webRoot, recursive: true);
    }
}
