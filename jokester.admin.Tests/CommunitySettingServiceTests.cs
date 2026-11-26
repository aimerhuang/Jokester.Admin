using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Community;
using jokester.admin.Application.Services;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class CommunitySettingServiceTests : IDisposable
{
    private readonly SqlSugarClient db = SiteNoticeTestDatabase.Open();
    private readonly Mock<ICurrentUser> user = new();
    private readonly CommunitySettingService service;
    private readonly long operatorId;

    public CommunitySettingServiceTests()
    {
        operatorId = db.Insertable(new SysUserEntity { UserName = "operator", IsSuperAdmin = true, Status = 1, CreatedAt = DateTime.UtcNow }).ExecuteReturnBigIdentity();
        user.SetupGet(x => x.UserId).Returns(operatorId);
        user.SetupGet(x => x.IsSuperAdmin).Returns(true);
        service = new(db, user.Object, new(TimeProvider.System));
    }

    private static SaveQqGroupConfig Save(int revision = 0, string group = "12345", bool bump = false, bool enabled = true) => new()
    {
        GroupName = " 群名 ", GroupNumber = group, Enabled = enabled, BumpPromptVersion = bump, Revision = revision,
        RemindIntervalDays = 4, Title = "测试标题", Description = "说明", JoinUrl = "https://qm.qq.com/q/example"
    };

    [Fact]
    public async Task DisabledConfigContainsOnlyFlagAndUpdatesInvalidateCache()
    {
        Assert.Equal("{\"enabled\":false}", JsonSerializer.Serialize(await service.GetAsync(default), SiteNoticeTestDatabase.Json));
        var first = await service.SaveAsync(Save(), default);
        Assert.Equal(1, first.Revision);
        Assert.Equal(1, first.PromptVersion);
        Assert.Equal("operator", first.UpdatedByName);
        Assert.Equal(DateTimeKind.Utc, first.UpdatedAt!.Value.Kind);
        var live = await service.GetAsync(default);
        Assert.True(live.Enabled);
        Assert.Equal("群名", live.GroupName);
        Assert.Equal("12345", live.GroupNumber);
        Assert.Equal(4, live.RemindIntervalDays);
        Assert.DoesNotContain("revision", JsonSerializer.Serialize(live, SiteNoticeTestDatabase.Json));
        Assert.Equal(2, (await service.SaveAsync(Save(first.Revision, bump: true, enabled: false), default)).PromptVersion);
        Assert.Equal("{\"enabled\":false}", JsonSerializer.Serialize(await service.GetAsync(default), SiteNoticeTestDatabase.Json));
        Assert.Equal(operatorId, (await service.GetAdminAsync(default)).UpdatedBy);
    }

    [Fact]
    public async Task InvalidConfigAndStaleRevisionPreserveCurrentState()
    {
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<AppException>(() => service.SaveAsync(Save(group: ""), default))).MachineCode);
        Assert.Equal("COMMUNITY_SETTING_REVISION_CONFLICT", (await Assert.ThrowsAsync<AppException>(() => service.SaveAsync(Save(5, bump: true), default))).MachineCode);
        Assert.Equal(0, (await service.GetAdminAsync(default)).Revision);
        Assert.Equal(1, (await service.GetAdminAsync(default)).PromptVersion);
    }

    [Fact]
    public async Task WritesRecheckCurrentDatabaseAdminAndReadsRequireClaim()
    {
        db.Updateable<SysUserEntity>().SetColumns(x => x.Status == 0).Where(x => x.Id == operatorId).ExecuteCommand();
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => service.SaveAsync(Save(), default))).Code);
        user.SetupGet(x => x.IsSuperAdmin).Returns(false);
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => service.GetAdminAsync(default))).Code);
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => service.SaveAsync(Save(), default))).Code);
        Assert.False((await service.GetAsync(default)).Enabled);
    }

    public void Dispose() => db.Dispose();
}
