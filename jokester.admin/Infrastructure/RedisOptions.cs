namespace jokester.admin.Infrastructure;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;

    public bool EnableInMemoryRefreshTokenFallback { get; set; }
}
