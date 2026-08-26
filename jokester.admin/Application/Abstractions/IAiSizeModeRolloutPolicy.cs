namespace jokester.admin.Application.Abstractions;

public interface IAiSizeModeRolloutPolicy
{
    AiImageClientContext GetClientContext();

    bool CanUseVersionedContract(AiImageClientContext context);

    bool CanUseAuto(AiImageClientContext context, string modelCode, string catalogVersion);
}

public sealed record AiImageClientContext(
    long? UserId,
    bool UnderstandsSizeModeV1,
    string Platform,
    string Version,
    string Build);
