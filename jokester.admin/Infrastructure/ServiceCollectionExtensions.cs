using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Services;
using jokester.admin.Infrastructure.Email;
using jokester.admin.Infrastructure.Mapping;
using jokester.admin.Infrastructure.PromptLibrary;
using jokester.admin.Infrastructure.Security;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SqlSugar;
using StackExchange.Redis;

namespace jokester.admin.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = RequireJwtOptions(configuration);
        var databaseOptions = RequireDatabaseOptions(configuration);
        var redisOptions = RequireRedisOptions(configuration);

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.SectionName));
        services.Configure<EmailValidationOptions>(configuration.GetSection(EmailValidationOptions.SectionName));
        services.AddSingleton<IValidateOptions<AiMediaStorageOptions>, AiMediaStorageOptionsValidator>();
        services.AddOptions<AiMediaStorageOptions>()
            .Bind(configuration.GetSection(AiMediaStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PromptLibraryOptions>, PromptLibraryOptionsValidator>();
        services.AddOptions<PromptLibraryOptions>()
            .Bind(configuration.GetSection(PromptLibraryOptions.SectionName))
            .PostConfigure(PromptLibraryConfiguration.ApplyFlatEnvironmentVariables)
            .ValidateOnStart();
        services.AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .Validate(
                options => options.PrimaryTimeoutSeconds is >= 10 and <= 240,
                "OpenAI:PrimaryTimeoutSeconds must be between 10 and 240 seconds.")
            .ValidateOnStart();
        services.AddOptions<AiCostControlOptions>()
            .Bind(configuration.GetSection(AiCostControlOptions.SectionName))
            .Validate(
                options => options.DailyImageLimitPerUser > 0
                    && options.DailyPointLimitPerUser > 0
                    && options.MaxConcurrentTasksPerUser > 0
                    && options.MaxQueuedTasks is > 0 and <= 1000
                    && options.MaxGlobalProviderConcurrency > 0
                    && options.IdempotencyTtlHours > 0
                    && options.ReservationTtlMinutes >= 120
                    && options.ProviderLeaseSeconds >= 300
                    && options.ProviderFailureThreshold > 0
                    && options.ProviderFailureWindowSeconds > 0
                    && options.ProviderCircuitOpenSeconds > 0,
                "AiCostControl values are outside the supported safety bounds.")
            .ValidateOnStart();
        services.AddOptions<AiPromptFilterOptions>()
            .Bind(configuration.GetSection(AiPromptFilterOptions.SectionName))
            .Validate(
                options => options.RefreshIntervalSeconds is >= 5 and <= 3600
                    && options.MaxSnapshotAgeMinutes is >= 1 and <= 1440
                    && options.MinimumActiveWordCount is >= 1 and <= 1_000_000,
                "AiPromptFilter values are outside the supported safety bounds.")
            .ValidateOnStart();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Redis");
            var options = ConfigurationOptions.Parse(redisOptions.ConnectionString);
            options.AbortOnConnectFail = false;

            var multiplexer = ConnectionMultiplexer.Connect(options);
            if (!multiplexer.IsConnected)
            {
                logger.LogWarning("Redis is not reachable at startup. The multiplexer will keep retrying in the background.");
            }
            return multiplexer;
        });
        services.AddScoped<ISqlSugarClient>(_ => new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = databaseOptions.ConnectionString,
            DbType = ParseDbType(databaseOptions.Provider),
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }));

        var mapsterConfig = TypeAdapterConfig.GlobalSettings;
        MapsterRegistration.Register(mapsterConfig);
        services.AddSingleton(mapsterConfig);
        services.AddScoped<IMapper, ServiceMapper>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IAiMediaPathResolver, AiMediaPathResolver>();
        services.AddSingleton<IPromptReadmeParser, MarkdigPromptReadmeParser>();
        services.AddScoped<IBlogMediaService, BlogMediaService>();
        services.AddSingleton<IRefreshTokenStore, ResilientRefreshTokenStore>();
        services.AddSingleton<IPermissionCacheInvalidator, RedisPermissionCacheInvalidator>();
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.Zero
                };
            });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    private static JwtOptions RequireJwtOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Missing Jwt configuration section.");

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            throw new InvalidOperationException("Missing Jwt:Issuer configuration.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException("Missing Jwt:Audience configuration.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new InvalidOperationException("Missing Jwt:SecretKey configuration.");
        }

        if (Encoding.UTF8.GetByteCount(options.SecretKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SecretKey must be at least 32 bytes.");
        }

        if (options.AccessTokenExpiresMinutes is < 10 or > 15)
        {
            throw new InvalidOperationException("Jwt:AccessTokenExpiresMinutes must be between 10 and 15.");
        }

        if (options.RefreshTokenExpiresDays <= 0)
        {
            throw new InvalidOperationException("Jwt:RefreshTokenExpiresDays must be greater than 0.");
        }

        return options;
    }

    private static DatabaseOptions RequireDatabaseOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
            ?? throw new InvalidOperationException("Missing Database configuration section.");

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            throw new InvalidOperationException("Missing Database:Provider configuration.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("Missing Database:ConnectionString configuration.");
        }

        _ = ParseDbType(options.Provider);
        return options;
    }

    private static RedisOptions RequireRedisOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>()
            ?? throw new InvalidOperationException("Missing Redis configuration section.");

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("Missing Redis:ConnectionString configuration.");
        }

        if (string.IsNullOrWhiteSpace(options.InstanceName))
        {
            throw new InvalidOperationException("Missing Redis:InstanceName configuration.");
        }

        return options;
    }

    private static DbType ParseDbType(string provider)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            "mysql" => DbType.MySql,
            _ => throw new InvalidOperationException($"Unsupported Database:Provider value: {provider}")
        };
    }
}
