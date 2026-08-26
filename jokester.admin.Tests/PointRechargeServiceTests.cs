using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.Security;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class PointRechargeServiceTests
{
    [Fact]
    public async Task GetPackagesAsync_ForIos_ReturnsOnlyMappedStoreKitProducts()
    {
        using var context = new TestContext(isSuperAdmin: false);
        var mappedPackage = new PointRechargePackageEntity
        {
            PackageCode = "basic",
            Name = "iOS Basic",
            Points = 1_000,
            PriceAmount = 6,
            Currency = "CNY",
            PurchaseUrl = "https://example.com/pay",
            Sort = 10,
            Status = 1,
            CreatedAt = DateTime.Now
        };
        mappedPackage.Id = context.Db.Insertable(mappedPackage).ExecuteReturnBigIdentity();
        context.Db.Insertable(new PointRechargePackageEntity
        {
            PackageCode = "web-only",
            Name = "Web only",
            Points = 200,
            PriceAmount = 12,
            Currency = "CNY",
            Sort = 20,
            Status = 1,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();
        context.Db.Insertable(new AppleIapProductEntity
        {
            PackageId = mappedPackage.Id,
            PackageCode = mappedPackage.PackageCode,
            AppleProductId = "cc.jokester.ai.credits.120",
            ProductType = "consumable",
            Points = 120,
            Environment = "Production",
            Status = 1,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();

        var result = await context.Service.GetPackagesAsync("ios", default);

        var package = Assert.Single(result);
        Assert.Equal("basic", package.Code);
        Assert.Equal(120, package.Points);
        Assert.Equal("apple_iap", package.PurchaseMethod);
        Assert.Equal("cc.jokester.ai.credits.120", package.AppleProductId);
        Assert.Equal("consumable", package.AppleProductType);
        Assert.True(package.PurchaseEnabled);
        Assert.True(package.Enabled);
    }

    [Fact]
    public async Task GetPackagesAsync_ForIos_RejectsMonthlyApplePointMismatch()
    {
        using var context = new TestContext(isSuperAdmin: false);
        SeedWebGenerationPackages(context);
        var monthly = context.Db.Queryable<PointRechargePackageEntity>()
            .Single(package => package.PackageCode == "monthly");
        context.Db.Insertable(new AppleIapProductEntity
        {
            PackageId = monthly.Id,
            PackageCode = monthly.PackageCode,
            AppleProductId = "cc.jokester.ai.monthly",
            ProductType = "consumable",
            Points = 4_999,
            Environment = "Production",
            Status = 1,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();

        var exception = await Assert.ThrowsAsync<AppException>(
            () => context.Service.GetPackagesAsync("ios", default));

        Assert.Equal(ErrorCodes.ServerError, exception.Code);
    }

    [Fact]
    public async Task GetPackagesAsync_ForWeb_ReturnsFourGenerationPackagesInConfiguredOrder()
    {
        using var context = new TestContext(isSuperAdmin: false);
        SeedWebGenerationPackages(context);

        var result = await context.Service.GetPackagesAsync("web", default);

        Assert.Collection(
            result,
            package => AssertPackage(package, "monthly", 5_000, 30),
            package => AssertPackage(package, "trial", 200, null),
            package => AssertPackage(package, "basic", 1_000, null),
            package => AssertPackage(package, "value", 3_600, null));
    }

    [Fact]
    public async Task GetPackagesAsync_ForWeb_ExcludesPackagesOutsideSelectableCatalog()
    {
        using var context = new TestContext(isSuperAdmin: false);
        SeedWebGenerationPackages(context);
        context.Db.Insertable(new PointRechargePackageEntity
        {
            PackageCode = "legacy-extra",
            Name = "Legacy Extra",
            Points = 999,
            PriceAmount = 9.99m,
            Currency = "CNY",
            Sort = 1,
            Status = 1,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();

        var result = await context.Service.GetPackagesAsync("web", default);

        Assert.Equal(new[] { "monthly", "trial", "basic", "value" }, result.Select(x => x.Code));
    }

    [Fact]
    public async Task GetPackagesAsync_ForWeb_FailsWhenSelectableCatalogIsIncomplete()
    {
        using var context = new TestContext(isSuperAdmin: false);
        SeedWebGenerationPackages(context);
        context.Db.Updateable<PointRechargePackageEntity>()
            .SetColumns(package => package.Status == 0)
            .Where(package => package.PackageCode == "value")
            .ExecuteCommand();

        var exception = await Assert.ThrowsAsync<AppException>(
            () => context.Service.GetPackagesAsync("web", default));

        Assert.Equal(ErrorCodes.ServerError, exception.Code);
    }

    [Fact]
    public async Task GetPackagesAsync_RejectsInvalidMonthlyPointsOrValidity()
    {
        using var context = new TestContext(isSuperAdmin: false);
        SeedWebGenerationPackages(context);
        context.Db.Updateable<PointRechargePackageEntity>()
            .SetColumns(package => package.Points == 4_999)
            .Where(package => package.PackageCode == "monthly")
            .ExecuteCommand();

        var exception = await Assert.ThrowsAsync<AppException>(
            () => context.Service.GetPackagesAsync("web", default));

        Assert.Equal(ErrorCodes.ServerError, exception.Code);
    }

    [Fact]
    public async Task GetPackagesAndCreateOrder_RejectMonthlyRepeatPointOverride()
    {
        using var context = new TestContext(isSuperAdmin: false);
        SeedWebGenerationPackages(context);
        context.Db.Updateable<PointRechargePackageEntity>()
            .SetColumns(package => package.RepeatPoints == 4_000)
            .Where(package => package.PackageCode == "monthly")
            .ExecuteCommand();

        var packageException = await Assert.ThrowsAsync<AppException>(
            () => context.Service.GetPackagesAsync("web", default));
        var orderException = await Assert.ThrowsAsync<AppException>(
            () => context.Service.CreateOrderAsync(
                new CreateRechargeOrderRequest { PackageCode = "monthly" },
                default));

        Assert.Equal(ErrorCodes.ServerError, packageException.Code);
        Assert.Equal(ErrorCodes.ServerError, orderException.Code);
        Assert.Empty(context.Db.Queryable<PointRechargeOrderEntity>().ToList());
    }

    [Theory]
    [InlineData("trial")]
    [InlineData("basic")]
    [InlineData("value")]
    public async Task GetPackagesAsync_RejectsExpiringPermanentPackage(string packageCode)
    {
        using var context = new TestContext(isSuperAdmin: false);
        SeedWebGenerationPackages(context);
        context.Db.Updateable<PointRechargePackageEntity>()
            .SetColumns(package => package.ValidityDays == 30)
            .Where(package => package.PackageCode == packageCode)
            .ExecuteCommand();

        var exception = await Assert.ThrowsAsync<AppException>(
            () => context.Service.GetPackagesAsync("web", default));

        Assert.Equal(ErrorCodes.ServerError, exception.Code);
    }

    [Theory]
    [InlineData("monthly", 5_000, 30)]
    [InlineData("trial", 200, null)]
    [InlineData("basic", 1_000, null)]
    [InlineData("value", 3_600, null)]
    public async Task IssueCodesAsync_AcceptsEachSelectablePackage(
        string packageCode,
        int expectedPoints,
        int? expectedValidityDays)
    {
        using var context = new TestContext(isSuperAdmin: true);
        SeedWebGenerationPackages(context);

        var result = await context.Service.IssueCodesAsync(new IssuePointRedeemCodesRequest
        {
            PackageCode = packageCode,
            Count = 1
        }, default);

        Assert.Equal(packageCode, result.PackageCode);
        Assert.Equal(expectedPoints, result.Points);
        Assert.Equal(expectedValidityDays, result.ValidityDays);
        var code = Assert.Single(context.Db.Queryable<PointRedeemCodeEntity>().ToList());
        Assert.Equal(expectedPoints, code.Points);
        Assert.Equal(expectedValidityDays ?? 0, code.PointValidityDays);
    }

    [Fact]
    public async Task IssueCodesAsync_RejectsEnabledPackageOutsideSelectableCatalog()
    {
        using var context = new TestContext(isSuperAdmin: true);
        context.Db.Insertable(new PointRechargePackageEntity
        {
            PackageCode = "legacy-extra",
            Name = "Legacy Extra",
            Points = 999,
            PriceAmount = 9.99m,
            Currency = "CNY",
            Status = 1,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();

        await Assert.ThrowsAsync<NotFoundException>(() => context.Service.IssueCodesAsync(
            new IssuePointRedeemCodesRequest { PackageCode = "legacy-extra", Count = 1 },
            default));
    }

    [Fact]
    public async Task IssueCodesAsync_WithCustomPoints_PersistsHashesAndReturnsPlaintextOnce()
    {
        using var context = new TestContext(isSuperAdmin: true);

        var result = await context.Service.IssueCodesAsync(new IssuePointRedeemCodesRequest
        {
            Points = 750,
            Count = 3,
            ExpiresAt = DateTime.Now.AddDays(30)
        }, default);

        Assert.Null(result.PackageCode);
        Assert.Equal(750, result.Points);
        Assert.Equal(3, result.Codes.Count);
        Assert.Equal(3, result.Codes.Distinct(StringComparer.Ordinal).Count());

        var persisted = await context.Db.Queryable<PointRedeemCodeEntity>().ToListAsync();
        Assert.Equal(3, persisted.Count);
        Assert.All(persisted, entity =>
        {
            Assert.Null(entity.PackageId);
            Assert.Null(entity.OrderId);
            Assert.Equal(750, entity.Points);
            Assert.Equal(1, entity.CreatedBy);
            Assert.DoesNotContain(result.Codes, code => entity.CodeHash.Contains(code, StringComparison.Ordinal));
            Assert.DoesNotContain(result.Codes, code => entity.CodeMask.Contains(code, StringComparison.Ordinal));
        });
        Assert.Equal(
            result.Codes.Select(PointRedeemCodeSecurity.Hash).OrderBy(value => value),
            persisted.Select(entity => entity.CodeHash).OrderBy(value => value));
    }

    [Fact]
    public async Task CustomPointCode_CanBeRedeemedExactlyOnce()
    {
        using var context = new TestContext(isSuperAdmin: true);
        context.Db.Updateable<SysUserEntity>()
            .SetColumns(x => x.PointBalance == 25)
            .Where(x => x.Id == 1)
            .ExecuteCommand();
        var issued = await context.Service.IssueCodesAsync(
            new IssuePointRedeemCodesRequest { Points = 475, Count = 1 }, default);

        var redeemed = await context.Service.RedeemAsync(
            new RedeemPointCodeRequest { Code = Assert.Single(issued.Codes) }, default);

        Assert.Equal(475, redeemed.AddedPoints);
        Assert.Equal(DateTimeKind.Utc, redeemed.RedeemedAt.Kind);
        Assert.Equal(500, redeemed.AvailablePoints);
        Assert.Equal(500, context.Db.Queryable<SysUserEntity>().Single().PointBalance);
        var detail = context.Db.Queryable<UserPointDetailEntity>().Single();
        Assert.Equal(475, detail.ChangePoints);
        Assert.Equal(500, detail.BalanceAfter);
        Assert.Equal("recharge", detail.Source);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.RedeemAsync(
            new RedeemPointCodeRequest { Code = issued.Codes[0] }, default));
        Assert.Equal(ErrorCodes.BadRequest, exception.Code);
        Assert.Equal(500, context.Db.Queryable<SysUserEntity>().Single().PointBalance);
        Assert.Single(context.Db.Queryable<UserPointDetailEntity>().ToList());
    }

    [Fact]
    public async Task CreateOrderAsync_ReturnsUtcTimestamps()
    {
        using var context = new TestContext(isSuperAdmin: false);
        context.Db.Insertable(new PointRechargePackageEntity
        {
            PackageCode = "basic",
            Name = "Web Basic",
            Points = 1_000,
            PriceAmount = 6,
            Currency = "CNY",
            PurchaseUrl = "https://example.test/pay/{orderNo}",
            Status = 1,
            CreatedAt = DateTime.Now
        }).ExecuteCommand();

        var result = await context.Service.CreateOrderAsync(
            new CreateRechargeOrderRequest { PackageCode = "basic" },
            default);

        Assert.Equal(DateTimeKind.Utc, result.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, result.ExpiresAt.Kind);
    }

    [Fact]
    public async Task IssueCodesAsync_WithPackage_RemainsCompatible()
    {
        using var context = new TestContext(isSuperAdmin: true);
        var package = new PointRechargePackageEntity
        {
            PackageCode = "basic",
            Name = "Basic",
            Points = 1_000,
            PriceAmount = 20,
            Currency = "CNY",
            Status = 1,
            CreatedAt = DateTime.Now
        };
        package.Id = context.Db.Insertable(package).ExecuteReturnBigIdentity();

        var result = await context.Service.IssueCodesAsync(new IssuePointRedeemCodesRequest
        {
            PackageCode = " BASIC ",
            Count = 2
        }, default);

        Assert.Equal("basic", result.PackageCode);
        Assert.Equal(1_000, result.Points);
        Assert.Equal(2, result.Codes.Count);
        Assert.All(context.Db.Queryable<PointRedeemCodeEntity>().ToList(), code => Assert.Equal(package.Id, code.PackageId));
    }

    [Fact]
    public async Task RedeemAsync_WithMonthlyPackage_CreatesThirtyDayPointBucketAndVipEntitlement()
    {
        using var context = new TestContext(isSuperAdmin: true);
        SeedWebGenerationPackages(context);
        var issued = await context.Service.IssueCodesAsync(new IssuePointRedeemCodesRequest
        {
            PackageCode = "monthly",
            Count = 1
        }, default);
        var issuedCode = Assert.Single(issued.Codes);
        var persistedCode = Assert.Single(await context.Db.Queryable<PointRedeemCodeEntity>().ToListAsync());
        Assert.Equal(30, issued.ValidityDays);
        Assert.Equal(30, persistedCode.PointValidityDays);
        var redeemStartedAt = DateTime.Now;

        var redeemed = await context.Service.RedeemAsync(
            new RedeemPointCodeRequest { Code = issuedCode },
            default);
        var redeemCompletedAt = DateTime.Now;

        Assert.Equal(5_000, redeemed.AddedPoints);
        Assert.Equal(5_000, redeemed.AvailablePoints);
        var bucket = Assert.Single(await context.Db.Queryable<PointBucketEntity>().ToListAsync());
        Assert.Equal(1, bucket.UserId);
        Assert.Equal("recharge", bucket.Source);
        Assert.Equal($"recharge:redeem:{persistedCode.Id}", bucket.BusinessKey);
        Assert.Equal(5_000, bucket.GrantedPoints);
        Assert.Equal(5_000, bucket.RemainingPoints);
        Assert.Equal(0, bucket.SpendPriority);
        Assert.InRange(
            bucket.ExpiresAt!.Value,
            redeemStartedAt.AddDays(30).AddSeconds(-1),
            redeemCompletedAt.AddDays(30).AddSeconds(1));
        Assert.NotNull(redeemed.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, redeemed.ExpiresAt.Value.Kind);
        Assert.Equal(
            ApiDateTime.FromLocalStorage(bucket.ExpiresAt.Value),
            redeemed.ExpiresAt.Value,
            TimeSpan.FromSeconds(1));
        var entitlement = Assert.Single(
            await context.Db.Queryable<SysUserMembershipEntitlementEntity>().ToListAsync());
        Assert.Equal(1, entitlement.UserId);
        Assert.Equal("monthly_vip", entitlement.TierCode);
        Assert.Equal("recharge", entitlement.Source);
        Assert.Equal(bucket.BusinessKey, entitlement.BusinessKey);
        Assert.Equal("active", entitlement.Status);
        Assert.Null(entitlement.RevokedAt);
        Assert.Equal(bucket.ExpiresAt.Value, entitlement.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RedeemAsync_WithPermanentPackage_CreatesTrackedPermanentBucket()
    {
        using var context = new TestContext(isSuperAdmin: true);
        SeedWebGenerationPackages(context);
        var issued = await context.Service.IssueCodesAsync(new IssuePointRedeemCodesRequest
        {
            PackageCode = "basic",
            Count = 1
        }, default);

        var redeemed = await context.Service.RedeemAsync(new RedeemPointCodeRequest
        {
            Code = Assert.Single(issued.Codes)
        }, default);

        Assert.Equal(1_000, redeemed.AddedPoints);
        Assert.Null(redeemed.ExpiresAt);
        var bucket = Assert.Single(await context.Db.Queryable<PointBucketEntity>().ToListAsync());
        Assert.Equal(1_000, bucket.GrantedPoints);
        Assert.Equal(1_000, bucket.RemainingPoints);
        Assert.Null(bucket.ExpiresAt);
        Assert.Equal(200, bucket.SpendPriority);
        Assert.Empty(await context.Db.Queryable<SysUserMembershipEntitlementEntity>().ToListAsync());
    }

    [Fact]
    public async Task IssueCodesAsync_RejectsExpiringOrderSnapshotForPermanentPackage()
    {
        using var context = new TestContext(isSuperAdmin: true);
        SeedWebGenerationPackages(context);
        var package = context.Db.Queryable<PointRechargePackageEntity>()
            .Single(value => value.PackageCode == "basic");
        var order = new PointRechargeOrderEntity
        {
            OrderNo = "R" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            UserId = 1,
            PackageId = package.Id,
            PackageCode = package.PackageCode,
            Points = package.Points,
            PointValidityDays = 30,
            PriceAmount = package.PriceAmount,
            Currency = package.Currency,
            Status = 0,
            ExpiresAt = DateTime.Now.AddHours(1),
            CreatedAt = DateTime.Now
        };
        order.Id = context.Db.Insertable(order).ExecuteReturnBigIdentity();

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.IssueCodesAsync(
            new IssuePointRedeemCodesRequest
            {
                PackageCode = package.PackageCode,
                OrderNo = order.OrderNo,
                Count = 1
            },
            default));

        Assert.Equal(ErrorCodes.ServerError, exception.Code);
        Assert.Empty(context.Db.Queryable<PointRedeemCodeEntity>().ToList());
        Assert.Equal(0, context.Db.Queryable<PointRechargeOrderEntity>().Single().Status);
    }

    [Fact]
    public async Task RedeemAsync_RejectsExpiringCodeSnapshotForPermanentPackage()
    {
        using var context = new TestContext(isSuperAdmin: true);
        SeedWebGenerationPackages(context);
        var package = context.Db.Queryable<PointRechargePackageEntity>()
            .Single(value => value.PackageCode == "basic");
        const string plaintextCode = "PERMANENT-SNAPSHOT-CODE";
        var redeemCode = new PointRedeemCodeEntity
        {
            CodeHash = PointRedeemCodeSecurity.Hash(plaintextCode),
            CodeMask = PointRedeemCodeSecurity.Mask(plaintextCode),
            PackageId = package.Id,
            Points = package.Points,
            PointValidityDays = 30,
            Status = 0,
            ExpiresAt = DateTime.Now.AddHours(1),
            CreatedBy = 1,
            CreatedAt = DateTime.Now
        };
        redeemCode.Id = context.Db.Insertable(redeemCode).ExecuteReturnBigIdentity();

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.RedeemAsync(
            new RedeemPointCodeRequest { Code = plaintextCode },
            default));

        Assert.Equal(ErrorCodes.ServerError, exception.Code);
        Assert.Equal(0, context.Db.Queryable<SysUserEntity>().Single().PointBalance);
        Assert.Empty(context.Db.Queryable<PointBucketEntity>().ToList());
        Assert.Equal(0, context.Db.Queryable<PointRedeemCodeEntity>().Single().Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public async Task IssueCodesAsync_RejectsInvalidCustomPoints(int points)
    {
        using var context = new TestContext(isSuperAdmin: true);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.IssueCodesAsync(
            new IssuePointRedeemCodesRequest { Points = points, Count = 1 }, default));

        Assert.Equal(ErrorCodes.BadRequest, exception.Code);
        Assert.Empty(await context.Db.Queryable<PointRedeemCodeEntity>().ToListAsync());
    }

    [Fact]
    public async Task IssueCodesAsync_RejectsPackageAndPointsTogether()
    {
        using var context = new TestContext(isSuperAdmin: true);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.IssueCodesAsync(
            new IssuePointRedeemCodesRequest { PackageCode = "basic", Points = 100, Count = 1 }, default));

        Assert.Equal(ErrorCodes.BadRequest, exception.Code);
    }

    [Fact]
    public async Task IssueCodesAsync_RejectsNonSuperAdministrator()
    {
        using var context = new TestContext(isSuperAdmin: false);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.IssueCodesAsync(
            new IssuePointRedeemCodesRequest { Points = 100, Count = 1 }, default));

        Assert.Equal(ErrorCodes.Forbidden, exception.Code);
        Assert.Empty(await context.Db.Queryable<PointRedeemCodeEntity>().ToListAsync());
    }

    [Fact]
    public async Task IssueCodesAsync_RejectsTokenWhoseUserWasDemotedInDatabase()
    {
        using var context = new TestContext(isSuperAdmin: true, databaseIsSuperAdmin: false);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.IssueCodesAsync(
            new IssuePointRedeemCodesRequest { Points = 100, Count = 1 }, default));

        Assert.Equal(ErrorCodes.Forbidden, exception.Code);
        Assert.Empty(await context.Db.Queryable<PointRedeemCodeEntity>().ToListAsync());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task IssueCodesAsync_RejectsTokenWhoseUserIsInactiveOrDeleted(int status, bool isDeleted)
    {
        using var context = new TestContext(
            isSuperAdmin: true,
            databaseIsSuperAdmin: true,
            databaseStatus: status,
            databaseIsDeleted: isDeleted);

        var exception = await Assert.ThrowsAsync<AppException>(() => context.Service.IssueCodesAsync(
            new IssuePointRedeemCodesRequest { Points = 100, Count = 1 }, default));

        Assert.Equal(ErrorCodes.Forbidden, exception.Code);
        Assert.Empty(await context.Db.Queryable<PointRedeemCodeEntity>().ToListAsync());
    }

    private static void SeedWebGenerationPackages(TestContext context)
    {
        var packages = new[]
        {
            new PointRechargePackageEntity
            {
                PackageCode = "monthly",
                Name = "Monthly",
                Points = 5_000,
                PriceAmount = 29.9m,
                Currency = "CNY",
                ValidityDays = 30,
                PurchaseUrl = "https://example.test/pay/{orderNo}",
                Sort = 10,
                Status = 1,
                CreatedAt = DateTime.Now
            },
            new PointRechargePackageEntity
            {
                PackageCode = "trial",
                Name = "Trial",
                Points = 200,
                RepeatPoints = 100,
                PriceAmount = 1m,
                Currency = "CNY",
                PurchaseUrl = "https://example.test/pay/{orderNo}",
                Sort = 20,
                Status = 1,
                CreatedAt = DateTime.Now
            },
            new PointRechargePackageEntity
            {
                PackageCode = "basic",
                Name = "Basic",
                Points = 1_000,
                PriceAmount = 10m,
                Currency = "CNY",
                PurchaseUrl = "https://example.test/pay/{orderNo}",
                Sort = 30,
                Status = 1,
                CreatedAt = DateTime.Now
            },
            new PointRechargePackageEntity
            {
                PackageCode = "value",
                Name = "Value",
                Points = 3_600,
                PriceAmount = 30m,
                Currency = "CNY",
                PurchaseUrl = "https://example.test/pay/{orderNo}",
                Sort = 40,
                Status = 1,
                CreatedAt = DateTime.Now
            }
        };

        context.Db.Insertable(packages).ExecuteCommand();
    }

    private static void AssertPackage(RechargePackageDto package, string code, int points, int? validityDays)
    {
        Assert.Equal(code, package.Code);
        Assert.Equal(points, package.Points);
        Assert.Equal(validityDays, package.ValidityDays);
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(
            bool isSuperAdmin,
            bool? databaseIsSuperAdmin = null,
            int databaseStatus = 1,
            bool databaseIsDeleted = false)
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute
            });
            Db.Ado.ExecuteCommand("""
                CREATE TABLE point_recharge_package (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    package_code TEXT NOT NULL,
                    name TEXT NOT NULL,
                    description TEXT NULL,
                    points INTEGER NOT NULL,
                    repeat_points INTEGER NULL,
                    price_amount NUMERIC NOT NULL,
                    currency TEXT NOT NULL,
                    validity_days INTEGER NULL,
                    bonus_percent INTEGER NOT NULL DEFAULT 0,
                    badge_code TEXT NULL,
                    benefits_json TEXT NULL,
                    purchase_url TEXT NULL,
                    is_featured INTEGER NOT NULL DEFAULT 0,
                    sort INTEGER NOT NULL DEFAULT 0,
                    status INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE point_redeem_code (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    code_hash TEXT NOT NULL,
                    code_mask TEXT NOT NULL,
                    package_id INTEGER NULL,
                    order_id INTEGER NULL,
                    points INTEGER NOT NULL,
                    point_validity_days INTEGER NULL,
                    status INTEGER NOT NULL,
                    redeemed_by_user_id INTEGER NULL,
                    expires_at TEXT NULL,
                    redeemed_at TEXT NULL,
                    created_by INTEGER NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );

                CREATE TABLE apple_iap_product (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    package_id INTEGER NOT NULL,
                    package_code TEXT NOT NULL,
                    apple_product_id TEXT NOT NULL,
                    product_type TEXT NOT NULL,
                    points INTEGER NOT NULL,
                    environment TEXT NOT NULL,
                    status INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE point_recharge_order (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    order_no TEXT NOT NULL,
                    user_id INTEGER NOT NULL,
                    package_id INTEGER NOT NULL,
                    package_code TEXT NOT NULL,
                    points INTEGER NOT NULL,
                    point_validity_days INTEGER NULL,
                    price_amount NUMERIC NOT NULL,
                    currency TEXT NOT NULL,
                    purchase_url TEXT NULL,
                    status INTEGER NOT NULL,
                    expires_at TEXT NOT NULL,
                    paid_at TEXT NULL,
                    fulfilled_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );

                CREATE TABLE sys_user (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_name TEXT NOT NULL,
                    nick_name TEXT NULL,
                    password_hash TEXT NOT NULL,
                    salt TEXT NULL,
                    email TEXT NULL,
                    phone TEXT NULL,
                    avatar_url TEXT NULL,
                    signature TEXT NULL,
                    point_balance INTEGER NOT NULL DEFAULT 0,
                    status INTEGER NOT NULL DEFAULT 1,
                    is_super_admin INTEGER NOT NULL DEFAULT 0,
                    last_login_time TEXT NULL,
                    last_login_ip TEXT NULL,
                    remark TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE sys_user_point_detail (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    change_points INTEGER NOT NULL,
                    balance_after INTEGER NOT NULL,
                    change_type TEXT NOT NULL,
                    source TEXT NOT NULL,
                    business_key TEXT NULL,
                    remark TEXT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE TABLE sys_user_point_bucket (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    source TEXT NOT NULL,
                    business_key TEXT NOT NULL,
                    granted_points INTEGER NOT NULL,
                    remaining_points INTEGER NOT NULL,
                    expired_points INTEGER NOT NULL DEFAULT 0,
                    expires_at TEXT NULL,
                    spend_priority INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );

                CREATE TABLE sys_user_point_bucket_usage (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bucket_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    business_key TEXT NOT NULL,
                    used_points INTEGER NOT NULL,
                    refunded_points INTEGER NOT NULL,
                    deferred_clawback_points INTEGER NOT NULL DEFAULT 0,
                    deferred_clawback_business_key TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );

                CREATE TABLE sys_user_membership_entitlement (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    tier_code TEXT NOT NULL,
                    source TEXT NOT NULL,
                    business_key TEXT NOT NULL UNIQUE,
                    starts_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    status TEXT NOT NULL,
                    revoked_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );
                """);

            Db.Insertable(new SysUserEntity
            {
                Id = 1,
                UserName = "code-issuer",
                PasswordHash = "unused",
                PointBalance = 0,
                Status = databaseStatus,
                IsSuperAdmin = databaseIsSuperAdmin ?? isSuperAdmin,
                CreatedAt = DateTime.Now,
                IsDeleted = databaseIsDeleted
            }).ExecuteCommand();

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(value => value.UserId).Returns(1);
            currentUser.SetupGet(value => value.IsSuperAdmin).Returns(isSuperAdmin);
            Service = new PointRechargeService(Db, currentUser.Object);
        }

        public SqlSugarClient Db { get; }

        public PointRechargeService Service { get; }

        public void Dispose() => Db.Dispose();
    }
}
