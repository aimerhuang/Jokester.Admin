using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Application.Services;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Tests;

public sealed class AiPromptFilterServiceTests
{
    [Fact]
    public async Task EmptyText_FailsClosedWhenNoSnapshotCanBeLoaded()
    {
        using var db = CreateDatabase();
        using var services = CreateServices(db);
        var filter = CreateFilter(services);

        await Assert.ThrowsAsync<AiPromptFilterUnavailableException>(() =>
            filter.EnsureAllowedAsync(string.Empty, "prompt", default));
    }

    [Fact]
    public async Task BatchCheck_UsesOneLoadedRevisionAndIdentifiesRejectedField()
    {
        using var db = CreateDatabase();
        CreateTables(db);
        await db.Insertable(new AiPromptSensitiveWordRevisionEntity
        {
            Id = 1,
            Revision = 7,
            UpdatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync();
        await db.Insertable(new AiPromptSensitiveWordEntity
        {
            Term = "rape",
            NormalizedTerm = "rape",
            TermKey = new string('d', 64),
            LanguageCode = "en",
            CategoryCode = "test",
            MatchMode = AiPromptFilterMatchModes.Word,
            Action = AiPromptFilterActions.Block,
            Severity = 5,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        }).ExecuteCommandAsync();
        using var services = CreateServices(db);
        var filter = CreateFilter(services);

        var emptyResult = await filter.EnsureAllowedAsync(string.Empty, "prompt", default);
        var exception = await Assert.ThrowsAsync<AiPromptRejectedException>(() =>
            filter.EnsureAllAllowedAsync(
                [
                    new AiPromptFilterText("prompt", "a landscape"),
                    new AiPromptFilterText("negativePrompt", "r.a.p.e")
                ],
                default));

        Assert.Equal(7, emptyResult.Revision);
        Assert.Equal("negativePrompt", exception.FieldName);
        Assert.Equal(7, exception.Result.Revision);
    }

    [Fact]
    public async Task BatchCheck_LexicalBlockIdentifiesRejectedField()
    {
        using var db = CreateDatabase();
        CreateTables(db);
        await db.Insertable(new AiPromptSensitiveWordRevisionEntity
        {
            Id = 1,
            Revision = 9,
            UpdatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync();
        await db.Insertable(new AiPromptSensitiveWordEntity
        {
            Term = "blocked phrase",
            NormalizedTerm = "blocked phrase",
            TermKey = new string('f', 64),
            LanguageCode = "en",
            CategoryCode = "test",
            MatchMode = AiPromptFilterMatchModes.Contains,
            Action = AiPromptFilterActions.Block,
            Severity = 5,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        }).ExecuteCommandAsync();
        using var services = CreateServices(db);
        var filter = CreateFilter(services);

        var exception = await Assert.ThrowsAsync<AiPromptRejectedException>(() =>
            filter.EnsureAllAllowedAsync(
                [
                    new AiPromptFilterText("prompt", "a normal landscape"),
                    new AiPromptFilterText("negativePrompt", "blocked phrase")
                ],
                default));

        Assert.Equal("negativePrompt", exception.FieldName);
    }

    [Fact]
    public async Task DisabledAuditCandidate_DoesNotEnterRuntimeSnapshot()
    {
        using var db = CreateDatabase();
        CreateTables(db);
        await db.Insertable(new AiPromptSensitiveWordRevisionEntity
        {
            Id = 1,
            Revision = 11,
            UpdatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync();
        await db.Insertable(new[]
        {
            new AiPromptSensitiveWordEntity
            {
                Term = "baseline block",
                NormalizedTerm = "baselineblock",
                TermKey = new string('a', 64),
                LanguageCode = "en",
                CategoryCode = "test",
                MatchMode = AiPromptFilterMatchModes.Word,
                Action = AiPromptFilterActions.Block,
                Severity = 1,
                Status = 1,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new AiPromptSensitiveWordEntity
            {
                Term = "幼女性交",
                NormalizedTerm = "幼女性交",
                TermKey = new string('b', 64),
                LanguageCode = "zh",
                CategoryCode = "sexual_minors",
                MatchMode = AiPromptFilterMatchModes.Compact,
                Action = AiPromptFilterActions.Audit,
                Severity = 5,
                Status = 0,
                SourceCode = "houbb-sensitive-word-data",
                SourceVersion = "fe6fc2921836217b8c90619db81b24af8b22d80f",
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        }).ExecuteCommandAsync();
        using var services = CreateServices(db);
        var filter = CreateFilter(services);

        var result = await filter.EnsureAllowedAsync("幼女性交", "prompt", default);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Match);
        Assert.Equal(11, result.Revision);
    }

    private static SqlSugarClient CreateDatabase()
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

    private static ServiceProvider CreateServices(ISqlSugarClient db)
    {
        return new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();
    }

    private static AiPromptFilterService CreateFilter(IServiceProvider services)
    {
        return new AiPromptFilterService(
            services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IConnectionMultiplexer>(),
            Options.Create(new RedisOptions()),
            Options.Create(new AiPromptFilterOptions()),
            NullLogger<AiPromptFilterService>.Instance);
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
}
