using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Common;
using jokester.admin.Application.DTOs.Users;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Moq;

namespace jokester.admin.Tests;

public sealed partial class UserServiceAuthorizationTests
{
    [Fact]
    public async Task AdminUsers_StatusAuditAndAccountUpdateRollBackTogether()
    {
        using var c = CreateContext(AdminId, true);
        c.Db.Ado.ExecuteCommand("CREATE TRIGGER reject_status BEFORE UPDATE OF status ON sys_user BEGIN SELECT RAISE(ABORT, 'test rollback'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => c.Admin.ChangeStatusAsync(BobId, new(0,"review"), default));
        Assert.Equal(1, c.GetUser(BobId).Status);
        Assert.Empty((await c.Admin.AuditsAsync(BobId, new(), default)).Items);
        c.RefreshTokenStore.Verify(x => x.RevokeUserSessionsAsync(BobId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(AliceId, 403)]
    public async Task AdminUsers_AllActionsRequireSuperAdmin(long? caller, int expected)
    {
        using var c = CreateContext(caller, false);
        Func<Task>[] actions = [() => c.Admin.ListAsync(new(), default), () => c.Admin.DetailAsync(BobId, default),
            () => c.Admin.SummaryAsync(BobId, new(), default), () => c.Admin.DetailsAsync(BobId, new(), default),
            () => c.Admin.ChangeStatusAsync(BobId, new(0, "reason"), default), () => c.Admin.AuditsAsync(BobId, new(), default)];
        foreach (var action in actions) Assert.Equal(expected, (await Assert.ThrowsAsync<AppException>(action)).Code);
    }

    [Fact]
    public async Task AdminUsers_ListFiltersAndSortsWithStableTies()
    {
        using var c = CreateContext(AdminId, true);
        c.Db.Ado.ExecuteCommand("UPDATE sys_user SET email = 'test%_@example.test', nick_name = 'special', point_balance = 8 WHERE id = 2");
        c.Db.Ado.ExecuteCommand("UPDATE sys_user SET is_deleted = 1 WHERE id = 4");
        c.Db.Ado.ExecuteCommand("INSERT INTO sys_user_membership_entitlement VALUES (2,'monthly_vip','active',NULL,'2020-01-01','2099-01-01'),(2,'monthly_vip','active',NULL,'2020-01-01','2099-02-01')");
        foreach (var keyword in new[] { "bo", "pecia", "example.test", "%", "_", "2" })
        {
            var list = await c.Admin.ListAsync(new() { Keyword = keyword, Status = 1, IsSuperAdmin = false }, default);
            var bob = Assert.Single(list.Items);
            Assert.Equal(BobId, bob.Id);
            Assert.Equal(8, bob.PointBalance);
            Assert.Equal(2, bob.Membership!.ExpiresAt.ToLocalTime().Month);
        }
        foreach (var sort in new[]{ "createdAt", "pointBalance", "lastLoginTime" })
        foreach (var order in new[]{ "asc", "desc" })
        {
            async Task<long[]> Ids(int page) => (await c.Admin.ListAsync(new() { SortBy = sort, SortOrder = order,
                PageSize = 2, PageIndex = page }, default)).Items.Select(x => x.Id).ToArray();
            var ids = (await Ids(1)).Concat(await Ids(2)).ToArray();
            var expected = sort == "pointBalance" ? (order == "asc" ? new long[]{5,3,1,2} : [2,5,3,1]) : [5,3,2,1];
            Assert.Equal(expected, ids);
        }
    }

    [Fact]
    public async Task AdminUsers_DisableIsAuditedOnceAndEnableRespectsPendingDeletion()
    {
        using var c = CreateContext(AdminId, true);
        foreach (var invalid in new[]{new AdminStatusRequest(2,"x"), new(0," "), new(1,new string('x',201))})
            Assert.Equal(400, (await Assert.ThrowsAsync<AppException>(() => c.Admin.ChangeStatusAsync(BobId, invalid, default))).Code);
        c.Db.Ado.ExecuteCommand("UPDATE sys_user SET status=1 WHERE id=5");
        foreach (var id in new[]{ AdminId, DisabledAdminId })
            Assert.Equal(MachineErrorCodes.UserStatusProtected, (await Assert.ThrowsAsync<AppException>(() => c.Admin.ChangeStatusAsync(id, new(0,"why"), default))).MachineCode);
        var before = c.GetUser(BobId).PointBalance;
        await c.Admin.ChangeStatusAsync(BobId, new(0,"  review  "), default);
        await c.Admin.ChangeStatusAsync(BobId, new(0,"duplicate"), default);
        await c.Admin.ChangeStatusAsync(BobId, new(0,null), default);
        Assert.Equal(0, c.GetUser(BobId).Status);
        Assert.Equal(before, c.GetUser(BobId).PointBalance);
        var audit = Assert.Single((await c.Admin.AuditsAsync(BobId, new(), default)).Items);
        Assert.Equal(("disable", "review", AdminId, "admin", DateTimeKind.Utc), (audit.Action, audit.Reason, audit.OperatorUserId, audit.OperatorUserName, audit.CreatedAt.Kind));
        c.RefreshTokenStore.Verify(x => x.RevokeUserSessionsAsync(BobId, It.IsAny<CancellationToken>()), Times.Once);
        foreach (var state in new[]{"scheduled","processing","failed","notification_pending"})
        {
            c.Db.Ado.ExecuteCommand("INSERT INTO account_deletion_request VALUES (2,@state)", new {state});
            Assert.Equal(MachineErrorCodes.UserDeletionInProgress, (await Assert.ThrowsAsync<AppException>(() => c.Admin.ChangeStatusAsync(BobId, new(1,null), default))).MachineCode);
            c.Db.Ado.ExecuteCommand("DELETE FROM account_deletion_request");
        }
        await c.Service.UpdateStatusAsync(BobId, new(){Status=1}, default);
        Assert.Equal(2, (await c.Admin.AuditsAsync(BobId,new(),default)).Total);
    }

    [Fact]
    public async Task AdminUsers_LegacyEditCannotDemoteAnotherSuperAdmin()
    {
        using var c = CreateContext(AdminId, true);
        c.Db.Ado.ExecuteCommand("UPDATE sys_user SET status=1 WHERE id=5");
        var demote = new UpdateUserInfoRequest { NickName = "demoted", Status = 1, IsSuperAdmin = false };
        var error = await Assert.ThrowsAsync<AppException>(() => c.Service.UpdateAsync(DisabledAdminId, demote, default));
        Assert.Equal(MachineErrorCodes.UserStatusProtected, error.MachineCode);
        Assert.True(c.GetUser(DisabledAdminId).IsSuperAdmin);
        Assert.Equal(MachineErrorCodes.UserStatusProtected, (await Assert.ThrowsAsync<AppException>(
            () => c.Admin.ChangeStatusAsync(DisabledAdminId, new(0, "why"), default))).MachineCode);
        Assert.Equal(1, c.GetUser(DisabledAdminId).Status);
        Assert.Empty((await c.Admin.AuditsAsync(DisabledAdminId, new(), default)).Items);
    }

    [Fact]
    public async Task AdminUsers_SummarySeparatesRefundSourcesAndUsesHalfOpenLocalRange()
    {
        using var c = CreateContext(AdminId, true);
        var from = new DateTimeOffset(2024,10,1,23,0,0,TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024,10,1)));
        var to = from.AddDays(1);
        foreach (var (source, change) in new[]{("recharge",100),("image_generate",-20),("image_generate",-10),
            ("image_refund",12),("apple_refund",-7),("sign_in_expire",-3),("new_source",2)})
        {
            var row = Detail(BobId, change, from.LocalDateTime);
            row.Source = source;
            c.Db.Insertable(row).ExecuteCommand();
        }
        c.Db.Insertable(Detail(BobId, 999, to.LocalDateTime)).ExecuteCommand();
        var result = await c.Admin.SummaryAsync(BobId,new(){ From=from,To=to }, default);
        Assert.Equal((100,18,3,7,2), (result.EarnedPoints,result.ImageSpentPoints,result.ExpiredPoints,result.ClawedBackPoints,result.ImageTaskCount));
        Assert.Equal("new_source", result.BySource.Last().Source);
        var entries = await c.Admin.DetailsAsync(BobId, new(){From=from,To=to,Source=["apple_refund","image_generate"],Direction="out"}, default);
        Assert.Equal(new[]{-7,-10,-20}, entries.Items.Select(x => x.ChangePoints));
        Assert.Equal(0,(await c.Admin.SummaryAsync(BobId,new(){From=to,To=to},default)).ImageTaskCount);
        Assert.Equal(400,(await Assert.ThrowsAsync<AppException>(()=>c.Admin.SummaryAsync(BobId,new(){From=to,To=from},default))).Code);
        Assert.DoesNotContain("businessKey", JsonSerializer.Serialize(entries), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminUsers_BalanceUsesSameSettlementForDisabledUser()
    {
        using var c = CreateContext(AdminId,true);
        c.Db.Ado.ExecuteCommand("UPDATE sys_user SET point_balance=100 WHERE id=2");
        c.Db.Ado.ExecuteCommand("INSERT INTO sys_user_point_bucket(user_id,source,business_key,granted_points,remaining_points,expires_at,spend_priority,created_at) VALUES(2,'recharge','expired',25,25,'2020-01-02',1,'2020-01-01')");
        var self = new PointService(c.Db, Mock.Of<ICurrentUser>(x=>x.UserId==BobId));
        var first = await c.Admin.DetailAsync(BobId,default);
        var expected = await self.GetBalanceAsync(default);
        Assert.Equal(75,first.Points.AvailablePoints);
        Assert.Equal(new AdminPoints(expected.AvailablePoints,expected.PermanentPoints,expected.ExpiringPoints,expected.NextExpiringPoints,expected.NextExpireAt),first.Points);
        await c.Admin.ChangeStatusAsync(BobId,new(0,"review"),default);
        var disabled = await c.Admin.DetailAsync(BobId,default);
        Assert.Equal(first.Points,disabled.Points);
        Assert.Equal(0,disabled.Status);
        var json = JsonSerializer.Serialize(disabled);
        Assert.DoesNotContain("passwordHash",json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("salt",json,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1,c.Db.Queryable<UserPointDetailEntity>().Count(x=>x.UserId==BobId && x.Source=="recharge_expire"));
        c.Db.Ado.ExecuteCommand("UPDATE sys_user SET is_deleted=1 WHERE id=2");
        await Assert.ThrowsAsync<NotFoundException>(()=>c.Admin.DetailAsync(BobId,default));
    }
}
