using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Mobile;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace jokester.admin.Controllers;

[AllowAnonymous]
[Route("api/mobile")]
public sealed class MobileController(
    ILegalDocumentService legalDocumentService,
    IOptions<MobileConfigurationOptions> mobileOptions,
    IOptions<AppleAppStoreOptions> appleOptions,
    IOptions<PromptLibraryOptions> promptLibraryOptions) : BaseApiController
{
    [HttpGet("config")]
    [ProducesResponseType(typeof(ApiResponse<MobileConfigurationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfiguration(
        [FromQuery] string? platform,
        [FromQuery] string? appVersion,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        ValidateAppVersion(appVersion);
        var legal = await legalDocumentService.GetCurrentAsync(platform, locale, cancellationToken);
        var options = mobileOptions.Value;
        return Success(new MobileConfigurationDto
        {
            MinimumSupportedVersion = options.MinimumSupportedVersion,
            LatestVersion = options.LatestVersion,
            MaintenanceMode = options.MaintenanceMode,
            Features = new MobileFeaturesDto
            {
                AppleIap = options.Features.AppleIap && appleOptions.Value.Enabled,
                AccountDeletion = options.Features.AccountDeletion,
                PromptLibrary = options.Features.PromptLibrary && promptLibraryOptions.Value.Enabled
            },
            LegalDocumentVersions = new MobileLegalDocumentVersionsDto
            {
                Privacy = legal.PrivacyPolicy.Version,
                Terms = legal.TermsOfService.Version,
                AiProcessing = legal.AiProcessingNotice?.Version ?? string.Empty
            }
        });
    }

    private static void ValidateAppVersion(string? appVersion)
    {
        if (string.IsNullOrWhiteSpace(appVersion)) return;

        var value = appVersion.Trim();
        if (value.Length > 32
            || !Version.TryParse(value, out var version)
            || version.Major < 0
            || version.Minor < 0)
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                MachineErrorCodes.ValidationError,
                "appVersion must be a valid dotted numeric version.");
        }
    }
}
