using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delegat.Infrastructure;

/// <summary>
/// Attaches the ApiKey bearer security requirement to Public API v1 operations
/// so Swagger UI sends Authorization after Authorize.
/// </summary>
public class PublicApiSwaggerSecurityFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? "";
        var isPublic = path.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase);

        if (!isPublic)
            return;

        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "ApiKey"
                    }
                },
                Array.Empty<string>()
            }
        });
    }
}
