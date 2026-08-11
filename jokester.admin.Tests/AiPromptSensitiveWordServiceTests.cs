using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiPromptFilter;
using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Application.Services;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiPromptSensitiveWordServiceTests
{
    [Fact]
    public async Task CreateStatusAndDelete_BumpRevisionAndRefreshSnapshot()
    {
        using var db = CreateDatabase();
        CreateTables(db);
        await db.Insertable(new AiPromptSensitiveWordRevisionEntity
        {
            Id = 1,
            Revision = 1,
            UpdatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync();
        await db.Insertable(new AiPromptSensitiveWordEntity
        {
            Term = "baseline",
            NormalizedTerm = "baseline",
            TermKey = new string('a', 64),
            LanguageCode = "en",
            CategoryCode = "test",
            MatchMode = AiPromptFilterMatchModes.Word,
            Action = AiPromptFilterActions.Block,
            Severity = 1,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        }).ExecuteCommandAsync();

        var promptFilter = new Mock<IAiPromptFilter>(MockBehavior.Strict);
        promptFilter
            .Setup(x => x.RefreshAsync(true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(42);
        var service = new AiPromptSensitiveWordService(
            db,
            promptFilter.Object,
            currentUser.Object,
            Options.Create(new AiPromptFilterOptions()),
            NullLogger<AiPromptSensitiveWordService>.Instance);

        var id = await service.CreateAsync(new SaveAiPromptSensitiveWordRequest
        {
            Term = "  测试规则  ",
            LanguageCode = "ZH",
            CategoryCode = "Safety",
            MatchMode = AiPromptFilterMatchModes.Contains,
            Action = AiPromptFilterActions.Block,
            Severity = 4,
            Status = 1
        }, default);

        var created = await db.Queryable<AiPromptSensitiveWordEntity>().InSingleAsync(id);
        Assert.Equal("测试规则", created.Term);
        Assert.Equal("测试规则", created.NormalizedTerm);
        Assert.Equal("zh", created.LanguageCode);
        Assert.Equal("safety", created.CategoryCode);
        Assert.Equal(42, created.CreatedBy);
        Assert.Equal(2, await GetRevisionAsync(db));

        promptFilter
            .Setup(x => x.RefreshAsync(true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        await service.UpdateStatusAsync(id, new UpdateAiPromptSensitiveWordStatusRequest { Status = 0 }, default);
        Assert.Equal(0, (await db.Queryable<AiPromptSensitiveWordEntity>().InSingleAsync(id)).Status);
        Assert.Equal(3, await GetRevisionAsync(db));

        promptFilter
            .Setup(x => x.RefreshAsync(true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        await service.DeleteAsync(id, default);
        Assert.True((await db.Queryable<AiPromptSensitiveWordEntity>().InSingleAsync(id)).IsDeleted);
        Assert.Equal(4, await GetRevisionAsync(db));
        promptFilter.VerifyAll();
    }

    [Fact]
    public async Task DisablingLastActiveBlockRule_IsRejectedBeforeRevisionChanges()
    {
        using var db = CreateDatabase();
        CreateTables(db);
        await db.Insertable(new AiPromptSensitiveWordRevisionEntity
        {
            Id = 1,
            Revision = 8,
            UpdatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync();
        var id = await db.Insertable(new AiPromptSensitiveWordEntity
        {
            Term = "only rule",
            NormalizedTerm = "only rule",
            TermKey = new string('b', 64),
            LanguageCode = "en",
            CategoryCode = "test",
            MatchMode = AiPromptFilterMatchModes.Word,
            Action = AiPromptFilterActions.Block,
            Severity = 3,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        }).ExecuteReturnBigIdentityAsync();
        var service = new AiPromptSensitiveWordService(
            db,
            Mock.Of<IAiPromptFilter>(),
            Mock.Of<ICurrentUser>(),
            Options.Create(new AiPromptFilterOptions { MinimumActiveWordCount = 1 }),
            NullLogger<AiPromptSensitiveWordService>.Instance);

        await Assert.ThrowsAsync<jokester.admin.Common.Exceptions.ConflictException>(() =>
            service.UpdateStatusAsync(id, new UpdateAiPromptSensitiveWordStatusRequest { Status = 0 }, default));

        Assert.Equal(1, (await db.Queryable<AiPromptSensitiveWordEntity>().InSingleAsync(id)).Status);
        Assert.Equal(8, await GetRevisionAsync(db));
    }

    [Fact]
    public async Task Create_RemainsSuccessfulWhenPostCommitRefreshFails()
    {
        using var db = CreateDatabase();
        CreateTables(db);
        await db.Insertable(new AiPromptSensitiveWordRevisionEntity
        {
            Id = 1,
            Revision = 1,
            UpdatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync();
        await db.Insertable(new AiPromptSensitiveWordEntity
        {
            Term = "baseline",
            NormalizedTerm = "baseline",
            TermKey = new string('c', 64),
            LanguageCode = "en",
            CategoryCode = "test",
            MatchMode = AiPromptFilterMatchModes.Word,
            Action = AiPromptFilterActions.Block,
            Severity = 1,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        }).ExecuteCommandAsync();

        var promptFilter = new Mock<IAiPromptFilter>();
        promptFilter
            .Setup(x => x.RefreshAsync(true, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient refresh failure"));
        var service = new AiPromptSensitiveWordService(
            db,
            promptFilter.Object,
            Mock.Of<ICurrentUser>(),
            Options.Create(new AiPromptFilterOptions()),
            NullLogger<AiPromptSensitiveWordService>.Instance);

        var id = await service.CreateAsync(new SaveAiPromptSensitiveWordRequest
        {
            Term = "committed rule",
            LanguageCode = "en",
            CategoryCode = "test",
            MatchMode = AiPromptFilterMatchModes.Word,
            Action = AiPromptFilterActions.Block,
            Severity = 2,
            Status = 1
        }, default);

        var created = await db.Queryable<AiPromptSensitiveWordEntity>().InSingleAsync(id);
        Assert.True(id > 0);
        Assert.Equal("committedrule", created.NormalizedTerm);
        Assert.Equal(2, await GetRevisionAsync(db));
        await Assert.ThrowsAsync<jokester.admin.Common.Exceptions.ConflictException>(() =>
            service.CreateAsync(new SaveAiPromptSensitiveWordRequest
            {
                Term = "committed-rule",
                LanguageCode = "en",
                CategoryCode = "test",
                MatchMode = AiPromptFilterMatchModes.Word,
                Action = AiPromptFilterActions.Block,
                Severity = 2,
                Status = 1
            }, default));
        Assert.Equal(2, await GetRevisionAsync(db));
    }

    private SqlSugarClient CreateDatabase()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute
        });
        db.Ado.Open();
        return db;
    }

    private static void CreateTables(ISqlSugarClient db)
    {
        db.Ado.ExecuteCommand("""
            CREATE TABLE ai_prompt_sensitive_word (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              term TEXT NOT NULL,
              normalized_term TEXT NOT NULL,
              term_key TEXT NOT NULL UNIQUE,
              language_code TEXT NOT NULL,
              category_code TEXT NOT NULL,
              match_mode TEXT NOT NULL,
              action TEXT NOT NULL,
              severity INTEGER NOT NULL,
              status INTEGER NOT NULL,
              source_code TEXT NULL,
              source_version TEXT NULL,
              remark TEXT NULL,
              created_by INTEGER NULL,
              updated_by INTEGER NULL,
              created_at TEXT NOT NULL,
              updated_at TEXT NULL,
              is_deleted INTEGER NOT NULL
            );
            CREATE TABLE ai_prompt_sensitive_word_revision (
              id INTEGER PRIMARY KEY,
              revision INTEGER NOT NULL,
              updated_by INTEGER NULL,
              updated_at TEXT NOT NULL
            );
            """);
    }

    private static async Task<long> GetRevisionAsync(ISqlSugarClient db)
    {
        return await db.Queryable<AiPromptSensitiveWordRevisionEntity>()
            .Where(x => x.Id == 1)
            .Select(x => x.Revision)
            .FirstAsync();
    }
}
