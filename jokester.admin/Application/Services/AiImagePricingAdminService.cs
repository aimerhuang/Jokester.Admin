using System.Security.Cryptography;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiImagePricingAdminService(ISqlSugarClient db, ICurrentUser currentUser,
    IHttpContextAccessor httpContext, IOptions<AiImagePricingAdminOptions> options)
{
    private sealed record WriteContext(AiImagePricingSettingEntity Setting, long Operator, string Reason, string BatchId);
    private static AppException Invalid(string message) => new(422, "VALIDATION_FAILED", message);
    private static AppException Forbidden() => new(403, MachineErrorCodes.ResourceForbidden, "只有有效的超级管理员可管理生图定价");
    private static AppException Conflict() => new(409, "PRICE_REVISION_CONFLICT", "价格或策略已更新，请刷新后重试");

    private long OperatorId() => currentUser is { IsSuperAdmin: true, UserId: long id } ? id : throw Forbidden();

    private async Task CheckAdmin(long id, bool forUpdate, CancellationToken ct)
    {
        var users = db.Queryable<SysUserEntity>();
        if (forUpdate) users = users.TranLock(DbLockType.Wait);
        var user = await users.FirstAsync(x => x.Id == id, ct);
        if (user is not { IsSuperAdmin: true, Status: 1, IsDeleted: false }) throw Forbidden();
    }

    private async Task<AiImagePricingSettingEntity> Setting(bool forUpdate, CancellationToken ct)
    {
        var settings = db.Queryable<AiImagePricingSettingEntity>();
        if (forUpdate) settings = settings.TranLock(DbLockType.Wait);
        return await settings.FirstAsync(x => x.Id == 1, ct) ?? throw new AppException(503, "请先初始化定价配置");
    }

    private async Task<T> Transaction<T>(Func<Task<T>> work)
    {
        await db.Ado.BeginTranAsync();
        try
        {
            var result = await work();
            await db.Ado.CommitTranAsync();
            return result;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private Task<PricingWriteResult> Write(string? reason, Func<WriteContext, Task<IReadOnlyList<PricingRow>>> change, CancellationToken ct)
    {
        var id = OperatorId();
        return Transaction(async () =>
        {
            var setting = await Setting(true, ct);
            await CheckAdmin(id, true, ct);
            var text = reason?.Trim();
            if (text is not { Length: >= 1 and <= 200 }) throw Invalid("请填写 1 至 200 字的变更原因");
            var rows = await change(new(setting, id, text, Guid.NewGuid().ToString("N")));
            setting.PricingRevision = checked(setting.PricingRevision + 1);
            await db.Updateable(setting).ExecuteCommandAsync(ct);
            return new PricingWriteResult(setting.PricingRevision, rows);
        });
    }

    private static string ModelCode(string? raw) => string.IsNullOrWhiteSpace(raw) || raw.Trim().Length > 100
        ? throw Invalid("请填写有效的模型编码") : AiImageModelConfigService.NormalizeModelCode(raw);

    private async Task<List<AiImageModelConfigEntity>> Routes(string code, CancellationToken ct)
    {
        var rows = await db.Queryable<AiImageModelConfigEntity>().Where(x => !x.IsDeleted && x.ModelCode == code)
            .OrderBy(x => x.Sort).Select(x => new AiImageModelConfigEntity { ModelCode = x.ModelCode, ResolutionCode = x.ResolutionCode,
                Provider = x.Provider, ModelName = x.ModelName, Status = x.Status }).ToListAsync(ct);
        if (rows.Count == 0) throw new NotFoundException("模型不存在");
        return rows;
    }

    private async Task<AiImageModelPolicyEntity?> Policy(string code, CancellationToken ct) =>
        await db.Queryable<AiImageModelPolicyEntity>().TranLock(DbLockType.Wait).FirstAsync(x => x.ModelCode == code, ct);

    internal static int? AutoVip(int points, int percent)
    {
        var discounted = (points * percent + 99) / 100;
        return discounted < points ? discounted : null;
    }

    private static PricingRow Row(AiImagePointPriceEntity x) => new(x.Id, x.ModelCode, x.ResolutionCode,
        x.QualityCode, x.Points, x.VipPoints, x.VipPriceMode, x.PriceAmount, x.Status, x.Revision);

    private static void ValidatePrice(int points, decimal amount, int status)
    {
        if (points is < 1 or > 100000 || status is not (0 or 1) || amount is < 0 or > 99999999.99m || decimal.Round(amount, 2) != amount)
            throw Invalid("积分应为 1 至 100000 的整数，折算金额最多两位小数，启用状态应为 0 或 1");
    }

    private bool Large(int oldPrice, int newPrice) =>
        Math.Abs((long)newPrice - oldPrice) * 100 > (long)oldPrice * options.Value.LargeChangePercent;

    private Task<int> Audit(WriteContext ctx, string target, string id, string? model, string action,
        object? before, object after, CancellationToken ct, string? resolution = null, string? quality = null)
        => db.Insertable(new AiImagePricingAuditEntity
        {
            BatchId = ctx.BatchId, TargetType = target, TargetId = id, ModelCode = model, ResolutionCode = resolution,
            QualityCode = quality, Action = action, BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = JsonSerializer.Serialize(after), Reason = ctx.Reason, OperatorUserId = ctx.Operator,
            OperatorIp = httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync(ct);

    private async Task AuditPrice(WriteContext ctx, PricingRow? old, AiImagePointPriceEntity row, string action, CancellationToken ct)
    {
        var before = new Dictionary<string, object?>();
        var after = new Dictionary<string, object?>();
        void Field(string key, object? previous, object? current)
        {
            if (old is not null && Equals(previous, current)) return;
            before[key] = previous;
            after[key] = current;
        }
        Field("points", old?.Points, row.Points);
        Field("vipPoints", old?.VipPoints, row.VipPoints);
        Field("vipPriceMode", old?.VipPriceMode, row.VipPriceMode);
        Field("priceAmount", old?.PriceAmount, row.PriceAmount);
        Field("status", old?.Status, row.Status);
        // The effective discount may change while the rounded VIP price stays equal.
        if (action == "vip_recalculate") { before["vipPoints"] = old?.VipPoints; after["vipPoints"] = row.VipPoints; }
        await Audit(ctx, "price", row.Id.ToString(), row.ModelCode, action, old is null ? null : before, after, ct,
            row.ResolutionCode, row.QualityCode);
    }

    private static bool Covers(AiImageModelConfigEntity route, string resolution) => route.Status == 1 &&
        (string.IsNullOrEmpty(route.ResolutionCode) || route.ResolutionCode == resolution);

    public Task<PricingOverview> GetAsync(CancellationToken ct) => Transaction(async () =>
    {
        await CheckAdmin(OperatorId(), false, ct);
        var settings = await Setting(false, ct);
        var routes = await db.Queryable<AiImageModelConfigEntity>().Where(x => !x.IsDeleted).OrderBy(x => x.Sort).OrderBy(x => x.Id)
            .Select(x => new AiImageModelConfigEntity { ModelCode = x.ModelCode, ModelName = x.ModelName, Provider = x.Provider,
                ResolutionCode = x.ResolutionCode, Status = x.Status }).ToListAsync(ct);
        var policies = (await db.Queryable<AiImageModelPolicyEntity>().ToListAsync(ct)).ToDictionary(x => x.ModelCode);
        var prices = (await db.Queryable<AiImagePointPriceEntity>().Where(x => !x.IsDeleted).OrderBy(x => x.Sort).OrderBy(x => x.Id)
            .ToListAsync(ct)).ToLookup(x => x.ModelCode);
        var parameters = (await db.Queryable<AiImageParameterEntity>().Where(x => !x.IsDeleted && x.Status == 1)
            .OrderBy(x => x.Sort).ToListAsync(ct)).ToLookup(x => x.ParamType, x => x.ParamCode);
        var current = await db.Queryable<AiImageCurrentReleaseEntity, AiImageModelReleaseEntity>((c, r) => c.ModelReleaseId == r.Id)
            .Where((c, r) => r.Status == "published" && r.RevokedAt == null).Select((c, r) => r).ToListAsync(ct);
        var releaseIds = current.Select(x => x.Id).ToArray();
        var releases = (await db.Queryable<AiImageModelReleasePriceEntity>().Where(x => releaseIds.Contains(x.ModelReleaseId) && x.Status == 1)
            .OrderBy(x => x.Sort).ToListAsync(ct)).ToLookup(x => x.ModelReleaseId);
        var groups = new List<PricingModel>();
        foreach (var modelRoutes in routes.GroupBy(x => x.ModelCode))
        {
            var code = modelRoutes.Key;
            var protocol = AiImageModelConfigService.ResolveProviderProtocol(modelRoutes.First().Provider);
            var qualities = protocol == AiImageModelConfigService.GeminiImageProtocol ? [string.Empty] : parameters["quality"].ToArray();
            var existing = prices[code].Select(Row).ToArray();
            var present = existing.Select(x => new PricingSlot(x.ResolutionCode, x.QualityCode)).ToHashSet();
            var missing = (from res in parameters["resolution"] where modelRoutes.Any(r => Covers(r, res))
                           from quality in qualities select new PricingSlot(res, quality)).Except(present).ToArray();
            var policy = policies.GetValueOrDefault(code);
            var release = current.FirstOrDefault(x => x.ModelCode == code);
            var releaseRows = releases[release?.Id ?? 0].Select(x => new ReleasePricingRow(x.PricingMode,
                x.PricingMode == "auto" ? null : x.ResolutionCode, x.QualityCode, x.Points)).ToArray();
            var legacyContract = protocol == AiImageModelConfigService.GeminiImageProtocol ? "legacy-aspect-auto" : "legacy-explicit-v1";
            groups.Add(new(code, modelRoutes.First().ModelName, protocol, release?.SizeContractVersion ?? legacyContract,
                modelRoutes.Count(x => x.Status == 1), policy?.AccessLevel ?? "all", policy?.VipDiscountPercent,
                policy?.VipDiscountPercent ?? settings.DefaultVipDiscountPercent, policy?.Revision ?? 0, existing, missing, releaseRows));
        }
        return new PricingOverview(settings.DefaultVipDiscountPercent, settings.Revision, settings.PricingRevision,
            options.Value.LargeChangePercent, groups);
    });

    public Task<PricingWriteResult> CreateAsync(CreatePricingRow request, CancellationToken ct) => Write(request.Reason, async ctx =>
    {
        var code = ModelCode(request.ModelCode);
        var routes = await Routes(code, ct);
        var policy = await Policy(code, ct);
        var res = request.ResolutionCode?.Trim().ToLowerInvariant() ?? "";
        var quality = request.QualityCode?.Trim().ToLowerInvariant() ?? "";
        var parameters = await db.Queryable<AiImageParameterEntity>().Where(x => !x.IsDeleted && x.Status == 1).ToListAsync(ct);
        var nano = AiImageModelConfigService.ResolveProviderProtocol(routes[0].Provider) == AiImageModelConfigService.GeminiImageProtocol;
        if (!parameters.Any(x => x.ParamType == "resolution" && x.ParamCode == res) ||
            (nano ? quality.Length != 0 : !parameters.Any(x => x.ParamType == "quality" && x.ParamCode == quality)))
            throw Invalid("当前协议不支持指定的分辨率或画质");
        ValidatePrice(request.Points, request.PriceAmount, request.Status);
        var vipPoints = AutoVip(request.Points, policy?.VipDiscountPercent ?? ctx.Setting.DefaultVipDiscountPercent);
        // The unique slot key also covers soft-deleted rows, so a deleted slot is restored in place instead of inserted.
        var existing = await db.Queryable<AiImagePointPriceEntity>().TranLock(DbLockType.Wait)
            .FirstAsync(x => x.ModelCode == code && x.ResolutionCode == res && x.QualityCode == quality, ct);
        if (existing is { IsDeleted: false }) throw new AppException(409, "PRICE_ALREADY_EXISTS", "价格档已存在");
        if (existing is not null)
        {
            var deleted = new { isDeleted = true, points = existing.Points, vipPoints = existing.VipPoints,
                vipPriceMode = existing.VipPriceMode, priceAmount = existing.PriceAmount, status = existing.Status };
            existing.IsDeleted = false;
            existing.Points = request.Points;
            existing.VipPoints = vipPoints;
            existing.VipPriceMode = "follow";
            existing.PriceAmount = request.PriceAmount;
            existing.Status = request.Status;
            await SavePrice(ctx, existing, ct);
            await Audit(ctx, "price", existing.Id.ToString(), code, "create", deleted,
                new { isDeleted = false, points = existing.Points, vipPoints = existing.VipPoints, vipPriceMode = existing.VipPriceMode,
                    priceAmount = existing.PriceAmount, status = existing.Status }, ct, res, quality);
            return [Row(existing)];
        }
        var row = new AiImagePointPriceEntity { ModelCode = code, ResolutionCode = res, QualityCode = quality, Points = request.Points,
            VipPoints = vipPoints, VipPriceMode = "follow",
            PriceAmount = request.PriceAmount, Status = request.Status, Revision = 1, CreatedAt = DateTime.Now, UpdatedBy = ctx.Operator };
        row.Id = await db.Insertable(row).ExecuteReturnBigIdentityAsync();
        await AuditPrice(ctx, null, row, "create", ct);
        return [Row(row)];
    }, ct);

    public Task<PricingWriteResult> BatchAsync(BatchPricingRequest request, CancellationToken ct) => Write(request.Reason, async ctx =>
    {
        if (request.Items is not { Count: >= 1 and <= 200 } items || items.Any(x => x is null || x.Id <= 0 || x.Revision is null) ||
            items.Select(x => x.Id).Distinct().Count() != items.Count) throw Invalid("每批须有 1 至 200 个不同的价格档及其版本");
        var edits = items.ToDictionary(x => x.Id);
        var ids = edits.Keys.ToArray();
        var modelCodes = (await db.Queryable<AiImagePointPriceEntity>().Where(x => !x.IsDeleted && ids.Contains(x.Id))
            .Select(x => x.ModelCode).ToListAsync(ct)).Distinct().Order().ToArray();
        var liveModels = await db.Queryable<AiImageModelConfigEntity>().Where(x => !x.IsDeleted && modelCodes.Contains(x.ModelCode))
            .Select(x => x.ModelCode).Distinct().ToListAsync(ct);
        if (modelCodes.Except(liveModels, StringComparer.OrdinalIgnoreCase).Any()) throw new NotFoundException("模型不存在");
        var policies = (await db.Queryable<AiImageModelPolicyEntity>().Where(x => modelCodes.Contains(x.ModelCode))
            .OrderBy(x => x.ModelCode).TranLock(DbLockType.Wait).ToListAsync(ct)).ToDictionary(x => x.ModelCode);
        var rows = await db.Queryable<AiImagePointPriceEntity>().Where(x => !x.IsDeleted && ids.Contains(x.Id)).OrderBy(x => x.Id)
            .TranLock(DbLockType.Wait).ToListAsync(ct);
        if (rows.Count != items.Count || rows.Any(x => x.Revision != edits[x.Id].Revision)) throw Conflict();
        var disabled = new HashSet<(string Model, string Resolution)>();
        var updated = new List<PricingRow>();
        foreach (var row in rows)
        {
            var edit = edits[row.Id];
            var old = Row(row);
            ValidatePrice(edit.Points, edit.PriceAmount ?? row.PriceAmount, edit.Status ?? row.Status);
            if (edit.VipPriceMode is not ("manual" or "follow")) throw Invalid("VIP 价格模式无效");
            // Omitting vipPoints must not silently remove a manual discount; clearing it requires an explicit null.
            if (edit.VipPriceMode == "manual" && !edit.HasVipPoints) throw Invalid("手动 VIP 价须提交 vipPoints，无优惠请显式传 null");
            if (edit.VipPriceMode == "manual" && edit.VipPoints is < 1) throw Invalid("VIP 积分必须为正整数");
            if (edit.VipPriceMode == "manual" && edit.VipPoints >= edit.Points)
                throw new AppException(422, "VIP_PRICE_NOT_LOWER", "手动 VIP 价须低于标准价，请同步调整或恢复自动价");
            var percent = policies.GetValueOrDefault(row.ModelCode)?.VipDiscountPercent ?? ctx.Setting.DefaultVipDiscountPercent;
            var vip = edit.VipPriceMode == "manual" ? edit.VipPoints : AutoVip(edit.Points, percent);
            if (!request.ConfirmLargeChange && (Large(row.Points, edit.Points) ||
                (edit.VipPriceMode == "manual" && Large(row.VipPoints ?? row.Points, vip ?? edit.Points))))
                throw new AppException(422, "PRICE_CHANGE_TOO_LARGE", "请确认大幅调价");
            if (row.Status == 1 && edit.Status == 0) disabled.Add((row.ModelCode, row.ResolutionCode));
            row.Points = edit.Points;
            row.VipPoints = vip;
            row.VipPriceMode = edit.VipPriceMode;
            row.PriceAmount = edit.PriceAmount ?? row.PriceAmount;
            row.Status = edit.Status ?? row.Status;
            if (old == Row(row)) continue;
            await SavePrice(ctx, row, ct);
            var action = old.Status == row.Status ? "update" : row.Status == 1 ? "enable" : "disable";
            await AuditPrice(ctx, old, row, action, ct);
            updated.Add(Row(row));
        }
        if (!request.ConfirmDisableLastPrice)
        {
            foreach (var (model, res) in disabled)
            {
                if (await db.Queryable<AiImagePointPriceEntity>().AnyAsync(x => !x.IsDeleted && x.ModelCode == model && x.ResolutionCode == res && x.Status == 1, ct)) continue;
                if (await db.Queryable<AiImageModelConfigEntity>().AnyAsync(x => !x.IsDeleted && x.ModelCode == model && x.Status == 1 &&
                    (x.ResolutionCode == res || x.ResolutionCode == ""), ct)) throw new AppException(409, "PRICE_LAST_ENABLED", "请确认停用此分辨率最后一个启用价格档");
            }
        }
        if (updated.Count == 0) throw Invalid("请选择有变更的价格档");
        return updated;
    }, ct);

    private async Task SavePrice(WriteContext ctx, AiImagePointPriceEntity row, CancellationToken ct)
    {
        row.Revision++;
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = ctx.Operator;
        await db.Updateable(row).ExecuteCommandAsync(ct);
    }

    private sealed record PreviewState(DiscountPreview Preview, List<AiImagePointPriceEntity> Rows, AiImageModelPolicyEntity? ModelPolicy);

    private async Task<PreviewState> BuildPreview(AiImagePricingSettingEntity settings, string? model, int? percent, bool locked, CancellationToken ct)
    {
        if (percent is < 1 or > 100 || (model is null && percent is null)) throw Invalid("请输入 1 至 100 的折扣百分比");
        if (model is not null) await Routes(model, ct);
        var policies = db.Queryable<AiImageModelPolicyEntity>().WhereIF(model is not null, x => x.ModelCode == model).OrderBy(x => x.ModelCode);
        if (locked) policies = policies.TranLock(DbLockType.Wait);
        var policyRows = await policies.ToListAsync(ct);
        if (locked && model is not null && policyRows.Count == 0)
        {
            // Reservations take a gap lock when they read a missing policy and then lock price rows. Inserting the
            // policy before locking prices keeps the insert from waiting on a reservation that waits on our prices.
            var created = NewPolicy(model);
            await db.Insertable(created).ExecuteCommandAsync(ct);
            policyRows.Add(created);
        }
        var policy = model is null ? null : policyRows.SingleOrDefault();
        var overridden = policyRows.Where(x => x.VipDiscountPercent.HasValue).Select(x => x.ModelCode).ToArray();
        var prices = db.Queryable<AiImagePointPriceEntity>().Where(x => !x.IsDeleted)
            .WhereIF(model is not null, x => x.ModelCode == model)
            .WhereIF(model is null && overridden.Length > 0, x => !overridden.Contains(x.ModelCode)).OrderBy(x => x.Id);
        if (locked) prices = prices.TranLock(DbLockType.Wait);
        var rows = await prices.ToListAsync(ct);
        var auto = rows.Where(x => x.VipPriceMode == "follow").ToList();
        var effective = percent ?? settings.DefaultVipDiscountPercent;
        var changes = auto.Select(x => new DiscountPreviewRow(x.Id, x.ModelCode, x.ResolutionCode, x.QualityCode,
            x.VipPoints, AutoVip(x.Points, effective))).ToArray();
        var manual = rows.Where(x => x.VipPriceMode == "manual").Select(x => new DiscountPreviewRow(x.Id, x.ModelCode,
            x.ResolutionCode, x.QualityCode, x.VipPoints, x.VipPoints)).ToArray();
        // Bind configuration and affected membership, not the global cache version or unrelated policies.
        var input = JsonSerializer.SerializeToUtf8Bytes(new { model, percent,
            defaultRevision = model is null || percent is null || policy?.VipDiscountPercent is null ? settings.Revision : (int?)null,
            modelRevision = policy?.Revision ?? 0, prices = auto.Select(x => new { x.Id, x.Revision }) });
        var token = Convert.ToHexStringLower(SHA256.HashData(input));
        return new(new(token, changes.Select(x => x.ModelCode).Distinct().Count(), changes.Length, changes, manual), auto, policy);
    }

    public Task<DiscountPreview> PreviewAsync(DiscountPreviewRequest request, CancellationToken ct) => Transaction(async () =>
    {
        await CheckAdmin(OperatorId(), false, ct);
        var model = request.ModelCode is null ? null : ModelCode(request.ModelCode);
        return (await BuildPreview(await Setting(false, ct), model, request.DiscountPercent, false, ct)).Preview;
    });

    private async Task SavePolicy(AiImageModelPolicyEntity value, bool isNew, WriteContext ctx, CancellationToken ct)
    {
        value.Revision++;
        value.UpdatedBy = ctx.Operator;
        value.UpdatedAt = DateTime.UtcNow;
        if (isNew) await db.Insertable(value).ExecuteCommandAsync(ct);
        else await db.Updateable(value).ExecuteCommandAsync(ct);
    }

    private static AiImageModelPolicyEntity NewPolicy(string model) => new()
    {
        ModelCode = model, RequiredTierCode = "monthly_vip", AccessLevel = "all"
    };

    public Task<PricingWriteResult> ApplyDiscountAsync(string? rawModel, ApplyDiscountRequest request, CancellationToken ct) => Write(request.Reason, async ctx =>
    {
        var model = rawModel is null ? null : ModelCode(rawModel);
        var state = await BuildPreview(ctx.Setting, model, request.DiscountPercent, true, ct);
        if (!string.Equals(request.PreviewToken, state.Preview.PreviewToken, StringComparison.Ordinal)) throw Conflict();
        var oldPercent = model is null ? ctx.Setting.DefaultVipDiscountPercent : state.ModelPolicy?.VipDiscountPercent;
        if (request.DiscountPercent == oldPercent) throw Invalid("折扣未发生变化");
        if (model is null)
        {
            ctx.Setting.DefaultVipDiscountPercent = request.DiscountPercent!.Value;
            ctx.Setting.Revision++;
            ctx.Setting.UpdatedAt = DateTime.UtcNow;
            ctx.Setting.UpdatedBy = ctx.Operator;
        }
        else
        {
            var policy = state.ModelPolicy ?? throw new InvalidOperationException("Locked model discount preview must hold a policy row.");
            policy.VipDiscountPercent = request.DiscountPercent;
            await SavePolicy(policy, false, ctx, ct);
        }
        await Audit(ctx, model is null ? "setting" : "model_policy", model ?? "1", model, "update",
            new { discountPercent = oldPercent }, new { discountPercent = request.DiscountPercent }, ct);
        var updated = new List<PricingRow>(state.Rows.Count);
        foreach (var row in state.Rows)
        {
            var before = Row(row);
            row.VipPoints = AutoVip(row.Points, request.DiscountPercent ?? ctx.Setting.DefaultVipDiscountPercent);
            await SavePrice(ctx, row, ct);
            await AuditPrice(ctx, before, row, "vip_recalculate", ct);
            updated.Add(Row(row));
        }
        return updated;
    }, ct);

    public Task<PricingWriteResult> AccessAsync(string rawModel, ChangeAccessRequest request, CancellationToken ct) => Write(request.Reason, async ctx =>
    {
        if (request.AccessLevel is not ("vip_only" or "all") || request.Revision is null) throw Invalid("请选择有效的访问策略并提交版本");
        var model = ModelCode(rawModel);
        await Routes(model, ct);
        var existing = await Policy(model, ct);
        var value = existing ?? NewPolicy(model);
        if (value.Revision != request.Revision) throw Conflict();
        var old = value.AccessLevel;
        if (old == request.AccessLevel) throw Invalid("模型访问策略未发生变化");
        value.AccessLevel = request.AccessLevel;
        await SavePolicy(value, existing is null, ctx, ct);
        await Audit(ctx, "model_policy", model, model, "update", new { accessLevel = old }, new { accessLevel = value.AccessLevel }, ct);
        return [];
    }, ct);

    public async Task<PagedResult<PricingAuditRow>> AuditsAsync(PricingAuditQuery filter, CancellationToken ct)
    {
        await CheckAdmin(OperatorId(), false, ct);
        var start = filter.StartTime?.UtcDateTime;
        var end = filter.EndTime?.UtcDateTime;
        var model = filter.ModelCode is null ? null : ModelCode(filter.ModelCode);
        var query = db.Queryable<AiImagePricingAuditEntity>()
            .WhereIF(model is not null, x => x.ModelCode == model)
            .WhereIF(start.HasValue, x => x.CreatedAt >= start)
            .WhereIF(end.HasValue, x => x.CreatedAt <= end)
            .WhereIF(filter.OperatorUserId.HasValue, x => x.OperatorUserId == filter.OperatorUserId);
        query = filter.ChangeType switch
        {
            null or "" => query,
            "points" => query.Where(x => x.TargetType == "price" && x.Action != "system_init" && x.AfterJson!.Contains("\"points\"")),
            "vipPoints" => query.Where(x => x.TargetType == "price" && (x.Action == "system_init" || x.AfterJson!.Contains("\"vipPoints\"") || x.AfterJson!.Contains("\"vipPriceMode\""))),
            "discount" => query.Where(x => x.TargetType == "setting" || (x.TargetType == "model_policy" && x.AfterJson!.Contains("\"discountPercent\""))),
            "access" => query.Where(x => x.TargetType == "model_policy" && x.AfterJson!.Contains("\"accessLevel\"")),
            _ => throw Invalid("不支持的审计变更类型")
        };
        RefAsync<int> total = 0;
        var rows = await query.OrderBy(x => x.Id, OrderByType.Desc).ToPageListAsync(filter.PageIndex, filter.PageSize, total);
        var userIds = rows.Select(x => x.OperatorUserId).Where(x => x != 0).Distinct().ToArray();
        var users = (await db.Queryable<SysUserEntity>().Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.UserName }).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.UserName);
        return new() { Total = total.Value, PageIndex = filter.PageIndex, PageSize = filter.PageSize,
            Items = rows.Select(x => new PricingAuditRow(x.Id, x.BatchId, x.TargetType, x.TargetId, x.ModelCode, x.ResolutionCode,
                x.QualityCode, x.Action, x.BeforeJson, x.AfterJson, x.Reason, x.OperatorUserId,
                x.OperatorUserId == 0 ? "系统" : users.GetValueOrDefault(x.OperatorUserId), ApiDateTime.FromUtcStorage(x.CreatedAt))).ToArray() };
    }
}
