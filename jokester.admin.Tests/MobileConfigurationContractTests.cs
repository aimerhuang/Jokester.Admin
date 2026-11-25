using System.Text.Json;
using jokester.admin.Application.DTOs.Mobile;
using jokester.admin.Common;
using jokester.admin.Controllers;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace jokester.admin.Tests;

public sealed class MobileConfigurationContractTests
{
    [Fact]
    public void MobileConfig_CombinesFeatureFlagsWithRuntimeCapabilities()
    {
        var controller = new MobileController(
            Options.Create(new MobileConfigurationOptions
            {
                MinimumSupportedVersion = "1.3.0",
                LatestVersion = "1.4.0",
                MaintenanceMode = true,
                Features = new MobileFeatureOptions
                {
                    AppleIap = true,
                    AccountDeletion = true,
                    PromptLibrary = true
                }
            }),
            Options.Create(new AppleAppStoreOptions { Enabled = false }),
            Options.Create(new PromptLibraryOptions { Enabled = false }));

        var action = controller.GetConfiguration("1.3.0");
        var response = Assert.IsType<ApiResponse<MobileConfigurationDto>>(
            Assert.IsType<OkObjectResult>(action).Value);

        Assert.Equal("1.3.0", response.Data!.MinimumSupportedVersion);
        Assert.True(response.Data.MaintenanceMode);
        Assert.False(response.Data.Features.AppleIap);
        Assert.False(response.Data.Features.PromptLibrary);
        Assert.True(response.Data.Features.AccountDeletion);
    }

    [Fact]
    public void MobileConfig_NoLongerExposesLegalDocumentVersions()
    {
        var json = JsonSerializer.Serialize(new MobileConfigurationDto(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("legalDocumentVersions", json, StringComparison.Ordinal);
    }
}
