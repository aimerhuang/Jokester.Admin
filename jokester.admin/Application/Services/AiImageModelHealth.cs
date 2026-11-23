using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Application.Services;

/// <summary>
/// Decides whether a settled AI image task is a model-side outcome that counts toward the public model status.
/// User-side failures (blocked prompts, missing consent, invalid inputs) and platform-side failures
/// (admission, queue binding, prompt filter outage) are excluded so they cannot mark a healthy model as down.
/// </summary>
public static class AiImageModelHealth
{
    public const int Success = 1;
    public const int Failure = 2;

    private static readonly HashSet<string> ModelSideStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "provider",
        "output"
    };

    public static int? ForVersioned(int finalStatus, string? failureStage)
    {
        if (finalStatus == 1)
        {
            return Success;
        }

        return failureStage is not null && ModelSideStages.Contains(failureStage) ? Failure : null;
    }

    public static int? ForLegacy(int finalStatus, bool modelSideFailure) =>
        finalStatus == 1 ? Success : modelSideFailure ? Failure : null;

    /// <summary>
    /// Classifies a legacy worker failure. Unknown exceptions (HTTP errors, timeouts, invalid provider output,
    /// broken route configuration) are attributed to the model.
    /// </summary>
    public static bool IsModelSideFailure(Exception exception) => exception switch
    {
        AiPromptRejectedException => false,
        AiPromptFilterUnavailableException => false,
        AppException appException when appException.Code == ErrorCodes.PreconditionFailed
            || appException.MachineCode == MachineErrorCodes.AiConsentRequired
            || appException.MachineCode == MachineErrorCodes.InputAssetInvalid
            || appException.Code == ErrorCodes.NotFound
            || appException.Code == ErrorCodes.Forbidden => false,
        _ => true
    };
}
