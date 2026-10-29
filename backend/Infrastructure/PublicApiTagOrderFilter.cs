using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delegat.Infrastructure;

/// <summary>
/// Keeps Public API Swagger sections in reading order instead of one alphabetical pile.
/// </summary>
public class PublicApiTagOrderFilter : IDocumentFilter
{
    private static readonly (string Name, string Description)[] Preferred =
    [
        ("Meta", "The key’s user and the stores that user can open."),
        ("Products", "Catalog cards and size variants."),
        ("Categories", "Local categories and their attributes."),
        ("Store attributes", "Attributes shared by every product in the store."),
        ("Groups", "Product groups and which cards belong to them."),
        ("Connections & operations", "Shops without credentials, product links, and background jobs."),
        ("Mappings", "Which marketplace category a product uses when its local category has more than one."),
        ("Sync", "Export and import jobs. Each call returns 202; poll Operations for the result."),
        ("Prices", "Live marketplace prices. Not stored in Delegat."),
        ("Orders", "Stored order snapshots, seller status changes, and packing labels."),
        ("Fulfillment", "Seller warehouses and marketplace drop-off offices. Live, not stored."),
        ("Shipments", "Supplies, shipping units, and their QR labels. Live, not stored."),
        ("Promotions", "Campaigns, coupons, and which products are enrolled. Live, not stored."),
    ];

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (!string.Equals(context.DocumentName, "v1-public", StringComparison.Ordinal))
            return;

        var present = swaggerDoc.Paths
            .SelectMany(p => p.Value.Operations.Values)
            .SelectMany(o => o.Tags)
            .Select(t => t.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (present.Count == 0)
            return;

        var described = (swaggerDoc.Tags ?? [])
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var preferredNames = Preferred.Select(t => t.Name).ToArray();
        var ordered = preferredNames
            .Where(name => present.Contains(name, StringComparer.Ordinal))
            .Concat(present.Where(name => !preferredNames.Contains(name, StringComparer.Ordinal)));

        swaggerDoc.Tags = ordered.Select(name =>
        {
            var tag = described.TryGetValue(name, out var existing) ? existing : new OpenApiTag { Name = name };
            var canonical = Preferred.FirstOrDefault(t => t.Name == name).Description;
            if (!string.IsNullOrEmpty(canonical))
                tag.Description = canonical;
            return tag;
        }).ToList();

        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.Schemas[PublicApiErrorResponsesFilter.SchemaId] = new OpenApiSchema
        {
            Type = "object",
            Description = "Error body for every Public API response that is not 2xx.",
            Required = new HashSet<string> { "error" },
            Properties =
            {
                ["error"] = new OpenApiSchema
                {
                    Type = "object",
                    Required = new HashSet<string> { "code", "message" },
                    Properties =
                    {
                        ["code"] = new OpenApiSchema
                        {
                            Type = "string",
                            Description = "Stable machine-readable identifier. Map this in the client; do not parse message.",
                            Example = new Microsoft.OpenApi.Any.OpenApiString("product_not_found"),
                        },
                        ["message"] = new OpenApiSchema
                        {
                            Type = "string",
                            Description = "Informational English text; may be generic. Clients should use the stable error code.",
                        },
                        ["details"] = new OpenApiSchema
                        {
                            Nullable = true,
                            Description = "Extra fields when the error carries them. Otherwise null.",
                        },
                    },
                },
            },
        };
    }
}
