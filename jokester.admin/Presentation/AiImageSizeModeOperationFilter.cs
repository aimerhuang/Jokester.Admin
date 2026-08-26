using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using jokester.admin.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace jokester.admin.Presentation;

public sealed class AiImageSizeModeOperationFilter : IOperationFilter
{
    private static readonly HashSet<string> VersionedActions =
    [
        nameof(AiImagesController.GetModels),
        nameof(AiImagesController.GetPricingOptions),
        nameof(AiImagesController.ResolveParameters),
        nameof(AiImagesController.Create),
        nameof(AiImagesController.Generate)
    ];

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(AiImagesController))
        {
            return;
        }

        if (VersionedActions.Contains(context.MethodInfo.Name))
        {
            AddClientCapabilityHeaders(operation);
        }

        if (context.MethodInfo.Name == nameof(AiImagesController.GetPricingOptions))
        {
            ApplyPricingResponseSchema(operation, context);
        }
    }

    private static void AddClientCapabilityHeaders(OpenApiOperation operation)
    {
        operation.Parameters ??= [];
        AddHeader(operation, "X-Client-Capabilities", "Protocol capabilities, including ai-size-mode-v1 when supported.");
        AddHeader(
            operation,
            "X-Client-Platform",
            "Client platform used for protocol compatibility checks.",
            ["web", "android", "ios"]);
        AddHeader(operation, "X-Client-Version", "Client semantic version used for protocol compatibility checks.");
        AddHeader(operation, "X-Client-Build", "Client build identifier used for protocol compatibility checks.");
    }

    private static void AddHeader(
        OpenApiOperation operation,
        string name,
        string description,
        IReadOnlyList<string>? allowedValues = null)
    {
        if (operation.Parameters.Any(parameter =>
                parameter.In == ParameterLocation.Header
                && string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = false,
            Description = description,
            Schema = new OpenApiSchema
            {
                Type = "string",
                Enum = allowedValues?.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList()
            }
        });
    }

    private static void ApplyPricingResponseSchema(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!operation.Responses.TryGetValue("200", out var response))
        {
            return;
        }

        var legacySchema = context.SchemaGenerator.GenerateSchema(
            typeof(ApiResponse<IReadOnlyList<AiImagePricingOptionDto>>),
            context.SchemaRepository);
        var versionedSchema = context.SchemaGenerator.GenerateSchema(
            typeof(ApiResponse<AiImageCatalogPricingResponse>),
            context.SchemaRepository);

        response.Description = "Legacy clients receive a flat pricing list; ai-size-mode-v1 clients receive a catalog-scoped envelope.";
        response.Content["application/json"] = new OpenApiMediaType
        {
            Schema = new OpenApiSchema
            {
                OneOf = [legacySchema, versionedSchema]
            }
        };
    }
}
