using System.Security.Cryptography;
using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiPromptFilter;
using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiPromptSensitiveWordService(
    ISqlSugarClient db,
    IAiPromptFilter promptFilter,
    ICurrentUser currentUser,
    IOptions<AiPromptFilterOptions> filterOptions,
    ILogger<AiPromptSensitiveWordService> logger) : IAiPromptSensitiveWordService
{
    public async Task<PagedResult<AiPromptSensitiveWordDto>> GetPageAsync(
        AiPromptSensitiveWordQuery query,
        CancellationToken cancellationToken)
    {
        RefAsync<int> total = 0;
        var keyword = NormalizeOptional(query.Keyword);
        var languageCode = NormalizeOptional(query.LanguageCode)?.ToLowerInvariant();
        var categoryCode = NormalizeOptional(query.CategoryCode)?.ToLowerInvariant();
        var matchMode = NormalizeOptional(query.MatchMode)?.ToLowerInvariant();
        var action = NormalizeOptional(query.Action)?.ToLowerInvariant();

        var items = await db.Queryable<AiPromptSensitiveWordEntity>()
            .Where(x => !x.IsDeleted)
            .WhereIF(keyword is not null, x => x.Term.Contains(keyword!) || x.Remark!.Contains(keyword!))
            .WhereIF(languageCode is not null, x => x.LanguageCode == languageCode)
            .WhereIF(categoryCode is not null, x => x.CategoryCode == categoryCode)
            .WhereIF(matchMode is not null, x => x.MatchMode == matchMode)
            .WhereIF(action is not null, x => x.Action == action)
            .WhereIF(query.Status.HasValue, x => x.Status == query.Status!.Value)
            .OrderByDescending(x => x.Severity)
            .OrderByDescending(x => x.Id)
            .Select(x => new AiPromptSensitiveWordDto
            {
                Id = x.Id,
                Term = x.Term,
                LanguageCode = x.LanguageCode,
                CategoryCode = x.CategoryCode,
                MatchMode = x.MatchMode,
                Action = x.Action,
                Severity = x.Severity,
                Status = x.Status,
                SourceCode = x.SourceCode,
                SourceVersion = x.SourceVersion,
                Remark = x.Remark,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        return new PagedResult<AiPromptSensitiveWordDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<AiPromptFilterStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var revision = await db.Queryable<AiPromptSensitiveWordRevisionEntity>()
            .Where(x => x.Id == 1)
            .Select(x => x.Revision)
            .FirstAsync(cancellationToken);
        var activeRuleCount = await db.Queryable<AiPromptSensitiveWordEntity>()
            .CountAsync(x => !x.IsDeleted && x.Status == 1, cancellationToken);

        return new AiPromptFilterStatusDto
        {
            Revision = revision,
            ActiveRuleCount = activeRuleCount
        };
    }

    public async Task<long> CreateAsync(SaveAiPromptSensitiveWordRequest request, CancellationToken cancellationToken)
    {
        var values = Validate(request);
        var userId = currentUser.UserId;
        var now = DateTime.UtcNow;
        long id;
        await db.Ado.BeginTranAsync();
        try
        {
            await LockRevisionAsync(cancellationToken);
            var existing = await db.Queryable<AiPromptSensitiveWordEntity>()
                .FirstAsync(x => x.TermKey == values.TermKey, cancellationToken);
            if (existing is not null && !existing.IsDeleted)
            {
                throw new ConflictException("Prompt filter rule already exists");
            }

            if (existing is not null)
            {
                Apply(existing, request, values, userId, now);
                existing.IsDeleted = false;
                existing.UpdatedAt = now;
                await db.Updateable(existing).ExecuteCommandAsync();
                id = existing.Id;
            }
            else
            {
                var entity = new AiPromptSensitiveWordEntity
                {
                    CreatedAt = now,
                    CreatedBy = userId
                };
                Apply(entity, request, values, userId, now);
                id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
            }

            await IncrementRevisionAsync(userId, now, cancellationToken);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        await RefreshAfterCommitAsync("create", id);
        return id;
    }

    public async Task UpdateAsync(long id, SaveAiPromptSensitiveWordRequest request, CancellationToken cancellationToken)
    {
        var values = Validate(request);
        var userId = currentUser.UserId;
        var now = DateTime.UtcNow;
        await db.Ado.BeginTranAsync();
        try
        {
            await LockRevisionAsync(cancellationToken);
            var entity = await RequireAsync(id, cancellationToken);
            var duplicate = await db.Queryable<AiPromptSensitiveWordEntity>()
                .AnyAsync(x => x.Id != id && x.TermKey == values.TermKey && !x.IsDeleted, cancellationToken);
            if (duplicate)
            {
                throw new ConflictException("Prompt filter rule already exists");
            }

            if (IsActiveBlock(entity.Status, entity.Action)
                && !IsActiveBlock(request.Status, values.Action))
            {
                await EnsureMinimumBlockRuleCountAsync(id, cancellationToken);
            }

            Apply(entity, request, values, userId, now);
            entity.UpdatedAt = now;
            await db.Updateable(entity).ExecuteCommandAsync();
            await IncrementRevisionAsync(userId, now, cancellationToken);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        await RefreshAfterCommitAsync("update", id);
    }

    public async Task UpdateStatusAsync(
        long id,
        UpdateAiPromptSensitiveWordStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Status is not 0 and not 1)
        {
            throw new AppException(ErrorCodes.BadRequest, "Status must be 0 or 1");
        }

        var userId = currentUser.UserId;
        var now = DateTime.UtcNow;
        await db.Ado.BeginTranAsync();
        try
        {
            await LockRevisionAsync(cancellationToken);
            var entity = await RequireAsync(id, cancellationToken);
            if (IsActiveBlock(entity.Status, entity.Action) && request.Status == 0)
            {
                await EnsureMinimumBlockRuleCountAsync(id, cancellationToken);
            }

            await db.Updateable<AiPromptSensitiveWordEntity>()
                .SetColumns(x => new AiPromptSensitiveWordEntity
                {
                    Status = request.Status,
                    UpdatedBy = userId,
                    UpdatedAt = now
                })
                .Where(x => x.Id == id && !x.IsDeleted)
                .ExecuteCommandAsync();
            await IncrementRevisionAsync(userId, now, cancellationToken);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        await RefreshAfterCommitAsync("status", id);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var now = DateTime.UtcNow;
        await db.Ado.BeginTranAsync();
        try
        {
            await LockRevisionAsync(cancellationToken);
            var entity = await RequireAsync(id, cancellationToken);
            if (IsActiveBlock(entity.Status, entity.Action))
            {
                await EnsureMinimumBlockRuleCountAsync(id, cancellationToken);
            }

            await db.Updateable<AiPromptSensitiveWordEntity>()
                .SetColumns(x => new AiPromptSensitiveWordEntity
                {
                    IsDeleted = true,
                    UpdatedBy = userId,
                    UpdatedAt = now
                })
                .Where(x => x.Id == id && !x.IsDeleted)
                .ExecuteCommandAsync();
            await IncrementRevisionAsync(userId, now, cancellationToken);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        await RefreshAfterCommitAsync("delete", id);
    }

    public async Task<TestAiPromptFilterResponse> TestAsync(
        TestAiPromptFilterRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new AppException(ErrorCodes.BadRequest, "Text is required");
        }
        if (request.Text.Length > AiImagePromptValidator.MaxLength)
        {
            throw new AppException(ErrorCodes.BadRequest, "Text is too long");
        }

        var result = await promptFilter.CheckAsync(request.Text, cancellationToken);
        return new TestAiPromptFilterResponse
        {
            IsAllowed = result.IsAllowed,
            Revision = result.Revision,
            RuleId = result.Match?.RuleId,
            LanguageCode = result.Match?.LanguageCode,
            CategoryCode = result.Match?.CategoryCode,
            MatchMode = result.Match?.MatchMode,
            Action = result.Match?.Action,
            Severity = result.Match?.Severity
        };
    }

    private async Task<AiPromptSensitiveWordEntity> RequireAsync(long id, CancellationToken cancellationToken)
    {
        return await db.Queryable<AiPromptSensitiveWordEntity>()
            .FirstAsync(x => x.Id == id && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException($"Prompt filter rule does not exist: {id}");
    }

    private async Task IncrementRevisionAsync(long? userId, DateTime now, CancellationToken cancellationToken)
    {
        var affected = await db.Updateable<AiPromptSensitiveWordRevisionEntity>()
            .SetColumns(x => x.Revision == x.Revision + 1)
            .SetColumns(x => new AiPromptSensitiveWordRevisionEntity
            {
                UpdatedBy = userId,
                UpdatedAt = now
            })
            .Where(x => x.Id == 1)
            .ExecuteCommandAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("Prompt filter revision row does not exist");
        }
    }

    private async Task LockRevisionAsync(CancellationToken cancellationToken)
    {
        var revision = await db.Queryable<AiPromptSensitiveWordRevisionEntity>()
            .TranLock(DbLockType.Wait)
            .FirstAsync(x => x.Id == 1, cancellationToken);
        if (revision is null)
        {
            throw new InvalidOperationException("Prompt filter revision row does not exist");
        }
    }

    private async Task RefreshAfterCommitAsync(string operation, long ruleId)
    {
        try
        {
            await promptFilter.RefreshAsync(force: true, publish: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Prompt filter rule change committed but immediate snapshot refresh failed. Operation={Operation}, RuleId={RuleId}, FailureType={FailureType}",
                operation,
                ruleId,
                ex.GetType().Name);
        }
    }

    private async Task EnsureMinimumBlockRuleCountAsync(long excludedId, CancellationToken cancellationToken)
    {
        var remaining = await db.Queryable<AiPromptSensitiveWordEntity>()
            .CountAsync(
                x => x.Id != excludedId
                    && !x.IsDeleted
                    && x.Status == 1
                    && x.Action == AiPromptFilterActions.Block,
                cancellationToken);
        if (remaining < filterOptions.Value.MinimumActiveWordCount)
        {
            throw new ConflictException(
                $"At least {filterOptions.Value.MinimumActiveWordCount} active block rules must remain");
        }
    }

    private static bool IsActiveBlock(int status, string? action)
    {
        return status == 1
            && string.Equals(action?.Trim(), AiPromptFilterActions.Block, StringComparison.OrdinalIgnoreCase);
    }

    private static ValidatedRule Validate(SaveAiPromptSensitiveWordRequest request)
    {
        var term = request.Term?.Trim() ?? string.Empty;
        if (term.Length is < 1 or > 255)
        {
            throw new AppException(ErrorCodes.BadRequest, "Term length must be between 1 and 255 characters");
        }

        var matchMode = (request.MatchMode ?? string.Empty).Trim().ToLowerInvariant();
        if (!AiPromptFilterMatchModes.IsSupported(matchMode))
        {
            throw new AppException(ErrorCodes.BadRequest, "Unsupported prompt filter match mode");
        }

        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (!AiPromptFilterActions.IsSupported(action))
        {
            throw new AppException(ErrorCodes.BadRequest, "Unsupported prompt filter action");
        }

        var languageCode = (request.LanguageCode ?? string.Empty).Trim().ToLowerInvariant();
        if (languageCode is not ("zh" or "en" or "mixed"))
        {
            throw new AppException(ErrorCodes.BadRequest, "Language code must be zh, en, or mixed");
        }

        var categoryCode = (request.CategoryCode ?? string.Empty).Trim().ToLowerInvariant();
        if (categoryCode.Length is < 1 or > 50)
        {
            throw new AppException(ErrorCodes.BadRequest, "Category code length must be between 1 and 50 characters");
        }
        if (categoryCode.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not '_' and not '-'))
        {
            throw new AppException(ErrorCodes.BadRequest, "Category code may contain only ASCII letters, digits, underscores, and hyphens");
        }

        if (request.Severity is < 1 or > 5)
        {
            throw new AppException(ErrorCodes.BadRequest, "Severity must be between 1 and 5");
        }

        if (request.Status is not 0 and not 1)
        {
            throw new AppException(ErrorCodes.BadRequest, "Status must be 0 or 1");
        }

        var normalizedTerm = AiPromptTextNormalizer.NormalizeRuleTerm(term, matchMode);
        if (normalizedTerm.Length is < 1 or > 512)
        {
            throw new AppException(ErrorCodes.BadRequest, "Term is empty or too long after normalization");
        }

        var termKeyBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{matchMode}:{normalizedTerm}"));
        var termKey = Convert.ToHexString(termKeyBytes).ToLowerInvariant();
        return new ValidatedRule(term, normalizedTerm, termKey, languageCode, categoryCode, matchMode, action);
    }

    private static void Apply(
        AiPromptSensitiveWordEntity entity,
        SaveAiPromptSensitiveWordRequest request,
        ValidatedRule values,
        long? userId,
        DateTime now)
    {
        entity.Term = values.Term;
        entity.NormalizedTerm = values.NormalizedTerm;
        entity.TermKey = values.TermKey;
        entity.LanguageCode = values.LanguageCode;
        entity.CategoryCode = values.CategoryCode;
        entity.MatchMode = values.MatchMode;
        entity.Action = values.Action;
        entity.Severity = request.Severity;
        entity.Status = request.Status;
        entity.SourceCode = Truncate(NormalizeOptional(request.SourceCode), 100);
        entity.SourceVersion = Truncate(NormalizeOptional(request.SourceVersion), 100);
        entity.Remark = Truncate(NormalizeOptional(request.Remark), 500);
        entity.UpdatedBy = userId;
        entity.UpdatedAt = entity.CreatedAt == default ? null : now;
        entity.IsDeleted = false;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int maxLength) => value is null || value.Length <= maxLength
        ? value
        : value[..maxLength];

    private sealed record ValidatedRule(
        string Term,
        string NormalizedTerm,
        string TermKey,
        string LanguageCode,
        string CategoryCode,
        string MatchMode,
        string Action);
}
