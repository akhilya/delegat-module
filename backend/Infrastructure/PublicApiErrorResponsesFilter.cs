using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delegat.Infrastructure;

/// <summary>
/// Documents the error body every Public API call can return, without repeating it on each action.
/// </summary>
public class PublicApiErrorResponsesFilter : IOperationFilter
{
    public const string SchemaId = "PublicApiError";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? "";
        var isPublic = path.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase);
        if (!isPublic)
            return;

        Add(operation, "401", "Missing, invalid, or revoked API key.");
        Add(operation, "403", "This key cannot open the store.");
        Add(operation, "429", "Too many requests for this key.");
        Add(operation, "503", "Public API is turned off for this instance or this plan.");
    }

    private static void Add(OpenApiOperation operation, string status, string description)
    {
        if (operation.Responses.ContainsKey(status))
            return;

        operation.Responses[status] = new OpenApiResponse
        {
            Description = description,
            Content =
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.Schema,
                            Id = SchemaId,
                        },
                    },
                },
            },
        };
    }
}
