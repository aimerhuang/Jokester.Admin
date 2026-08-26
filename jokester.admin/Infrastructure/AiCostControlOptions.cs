namespace jokester.admin.Infrastructure;

public sealed class AiCostControlOptions
{
    public const string SectionName = "AiCostControl";

    public int DailyImageLimitPerUser { get; set; } = 20;

    public int DailyPointLimitPerUser { get; set; } = 1000;

    public int MaxConcurrentTasksPerUser { get; set; } = 10;

    public int MaxQueuedTasks { get; set; } = 20;

    public int MaxGlobalProviderConcurrency { get; set; } = 4;

    public int IdempotencyTtlHours { get; set; } = 24;

    public int ReservationTtlMinutes { get; set; } = 1440;

    public int ProviderLeaseSeconds { get; set; } = 660;

    public int ProviderFailureThreshold { get; set; } = 5;

    public int ProviderFailureWindowSeconds { get; set; } = 300;

    public int ProviderCircuitOpenSeconds { get; set; } = 60;

    public int OutboxBindDeadlineMinutes { get; set; } = 120;
}
