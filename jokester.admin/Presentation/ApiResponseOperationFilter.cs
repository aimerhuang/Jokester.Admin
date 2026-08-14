using jokester.admin.Common;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace jokester.admin.Presentation;

public sealed class ApiResponseOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, string> ErrorResponses =
        new Dictionary<string, string>
        {
            ["400"] = "Validation error",
            ["401"] = "Authentication failed or expired",
            ["403"] = "Resource forbidden",
            ["409"] = "Conflict or idempotency conflict",
            ["412"] = "Precondition failed",
            ["422"] = "Content safety rejection",
            ["429"] = "Rate limited",
            ["500"] = "Server error",
            ["503"] = "Service unavailable"
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var schema = context.SchemaGenerator.GenerateSchema(
            typeof(ApiErrorResponse),
            context.SchemaRepository);

        foreach (var (statusCode, description) in ErrorResponses)
        {
            operation.Responses.TryAdd(statusCode, new OpenApiResponse
            {
                Description = description,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new() { Schema = schema }
                }
            });
        }
    }
}
