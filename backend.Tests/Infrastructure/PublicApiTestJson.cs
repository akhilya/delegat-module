using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Delegat.Tests.Infrastructure;

internal static class PublicApiTestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static JsonElement FromOk(IActionResult result)
    {
        var value = Assert.IsType<OkObjectResult>(result).Value
            ?? throw new InvalidOperationException("Empty OK body");
        return JsonSerializer.SerializeToElement(value, Options);
    }

    public static JsonElement FromObject(object? value)
    {
        return JsonSerializer.SerializeToElement(value ?? new { }, Options);
    }
}
