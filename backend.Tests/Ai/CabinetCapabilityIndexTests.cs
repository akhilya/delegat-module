using System.Text.Json;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class CabinetCapabilityIndexTests
{
    [Fact]
    public void Validate_ExistingJsonRoute_IsOk()
    {
        var index = CabinetCapabilities.Standard();

        var result = index.Validate("GET", "/products");

        Assert.Equal(CabinetCapabilityIndex.StatusOk, result.Status);
        Assert.Equal("/api/products", result.Path);
        Assert.NotNull(result.MatchedCapability);
    }

    [Fact]
    public void Validate_TemplateParameter_MatchesConcreteSegment()
    {
        var index = CabinetCapabilities.Standard();

        var result = index.Validate("GET", "/products/42");

        Assert.Equal(CabinetCapabilityIndex.StatusOk, result.Status);
        Assert.Equal("/api/products/{id}", result.MatchedCapability!.PathTemplate);
    }

    [Fact]
    public void Validate_UnknownRoute_IsRouteNotFoundWithBoundedAlternatives()
    {
        var index = CabinetCapabilities.Standard();

        var result = index.Validate("POST", "/store-files");

        Assert.Equal(CabinetCapabilityIndex.StatusRouteNotFound, result.Status);
        Assert.Equal(CabinetCapabilityIndex.ClassRouteNotFound, result.ErrorClass);
        Assert.True(result.Alternatives.Count <= CabinetCapabilityIndex.MaxAlternatives);
        // Alternatives are only validated callable actions — never a guess.
        Assert.All(result.Alternatives, a =>
            Assert.Equal(CabinetCapabilityIndex.StatusOk, index.Validate(a.Method, a.Path).Status));
    }

    [Fact]
    public void Validate_WrongMethod_IsMethodNotAllowed()
    {
        var index = CabinetCapabilities.Standard();

        var result = index.Validate("DELETE", "/products");

        Assert.Equal(CabinetCapabilityIndex.StatusMethodNotAllowed, result.Status);
        Assert.Equal(CabinetCapabilityIndex.ClassMethodNotAllowed, result.ErrorClass);
        Assert.Equal(["GET", "POST"], result.AllowedMethods);
    }

    [Fact]
    public void Validate_LiteralRouteBeatsParameterSibling_WrongVerb()
    {
        // GET /products/export-template must not validate against the generic
        // GET /products/{productId} — the literal POST route wins precedence,
        // so the honest answer is method_not_allowed, like endpoint routing.
        var index = new CabinetCapabilityIndex(CabinetActions.Provider(
            CabinetActions.Action(typeof(FakeCabinetController), "GetProduct", "/products/{productId}", "GET"),
            CabinetActions.Action(typeof(FakeCabinetController), "ExportTemplate", "/products/export-template", "POST")));

        var result = index.Validate("GET", "/products/export-template");

        Assert.Equal(CabinetCapabilityIndex.StatusMethodNotAllowed, result.Status);
        Assert.Equal(["POST"], result.AllowedMethods);
    }

    [Fact]
    public void Validate_IntConstraint_RejectsNonNumericSegment()
    {
        var index = new CabinetCapabilityIndex(CabinetActions.Provider(
            CabinetActions.Action(typeof(FakeCabinetController), "GetFile", "/files/{id:int}", "GET")));

        Assert.Equal(CabinetCapabilityIndex.StatusRouteNotFound,
            index.Validate("GET", "/files/abc").Status);
        Assert.Equal(CabinetCapabilityIndex.StatusOk,
            index.Validate("GET", "/files/42").Status);
    }

    [Fact]
    public void Validate_ConstrainedParameter_PreferredOverUnconstrained()
    {
        var index = new CabinetCapabilityIndex(CabinetActions.Provider(
            CabinetActions.Action(typeof(FakeCabinetController), "Wildcard", "/files/{name}", "GET"),
            CabinetActions.Action(typeof(FakeCabinetController), "ById", "/files/{id:int}", "POST")));

        var result = index.Validate("GET", "/files/42");

        Assert.Equal(CabinetCapabilityIndex.StatusMethodNotAllowed, result.Status);
        Assert.Equal(["POST"], result.AllowedMethods);
    }

    [Theory]
    [InlineData("PUT", "/store-storage/files")]
    [InlineData("PUT", "/products/9/files")]
    [InlineData("POST", "/products/import-preview")]
    public void Validate_MultipartRoute_IsMultipartRequired(string method, string path)
    {
        var index = CabinetCapabilities.Standard();

        var result = index.Validate(method, path);

        Assert.Equal(CabinetCapabilityIndex.StatusMultipartRequired, result.Status);
        Assert.Equal(CabinetCapabilityIndex.ClassTransportMismatch, result.ErrorClass);
        Assert.True(result.MatchedCapability!.RequiresMultipart);
        Assert.False(result.MatchedCapability.JsonCallable);
    }

    [Fact]
    public void Validate_MultipartRoute_OffersOnlyCallableAlternatives()
    {
        var index = CabinetCapabilities.Standard();

        var result = index.Validate("PUT", "/store-storage/files");

        var paths = result.Alternatives.Select(a => a.Method + " " + a.Path).ToList();
        Assert.Contains("POST /store-storage/files/from-text", paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("PUT ", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_BinaryDownload_IsBinaryDownload()
    {
        var index = CabinetCapabilities.Standard();

        var result = index.Validate("GET", "/files/9/download");

        Assert.Equal(CabinetCapabilityIndex.StatusBinaryDownload, result.Status);
        Assert.Equal(CabinetCapabilityIndex.ClassTransportMismatch, result.ErrorClass);
    }

    [Fact]
    public void Validate_DeniedRoute_IsRouteDenied()
    {
        var index = CabinetCapabilities.Standard();

        var result = index.Validate("DELETE", "/api/v1/products");

        Assert.Equal(CabinetCapabilityIndex.StatusRouteDenied, result.Status);
        Assert.Equal(CabinetCapabilityIndex.ClassForbidden, result.ErrorClass);
    }

    [Fact]
    public void Validate_ExtrasRoute_IsCallable()
    {
        var index = CabinetCapabilities.Standard();

        var result = index.Validate("POST", "/products/9/files");

        // The attach-staged-files route is synthetic (no MVC action) but JSON-callable.
        Assert.Equal(CabinetCapabilityIndex.StatusOk, result.Status);
    }

    [Fact]
    public void Validate_EmptyIndex_DegradesToOk()
    {
        var index = new CabinetCapabilityIndex();

        Assert.False(index.HasMvcRoutes);
        Assert.Equal(CabinetCapabilityIndex.StatusOk, index.Validate("DELETE", "/any/path").Status);
        Assert.Equal(CabinetCapabilityIndex.StatusRouteDenied, index.Validate("DELETE", "/api/v1/x").Status);
    }

    [Fact]
    public void PlanStepRejection_MultipartStep_NamesStepAndAlternatives()
    {
        var index = CabinetCapabilities.Standard();
        using var doc = JsonDocument.Parse("""
            [{"tool":"platform_request","description":"Upload","args":{"method":"PUT","path":"/store-storage/files"}}]
            """);

        var reason = index.PlanStepRejection(doc.RootElement.EnumerateArray().ToList());

        Assert.NotNull(reason);
        Assert.Contains("Plan not presented", reason);
        Assert.Contains("step 1", reason);
        Assert.Contains("PUT /store-storage/files", reason);
        Assert.Contains("requires multipart", reason);
        Assert.Contains("/store-storage/files/from-text", reason);
    }

    [Fact]
    public void PlanStepRejection_ValidAndNonPlatformSteps_Pass()
    {
        var index = CabinetCapabilities.Standard();
        using var doc = JsonDocument.Parse("""
            [
                {"tool":"platform_request","description":"List","args":{"method":"GET","path":"/products"}},
                {"tool":"run_python","description":"Compute","args":{"code":"print(1)"}}
            ]
            """);

        Assert.Null(index.PlanStepRejection(doc.RootElement.EnumerateArray().ToList()));
    }

    [Fact]
    public void PlanStepRejection_EmptyIndex_NeverRejects()
    {
        var index = new CabinetCapabilityIndex();
        using var doc = JsonDocument.Parse("""
            [{"tool":"platform_request","description":"Call","args":{"method":"POST","path":"/anything"}}]
            """);

        Assert.Null(index.PlanStepRejection(doc.RootElement.EnumerateArray().ToList()));
    }

    [Fact]
    public void Index_MultipartHit_IsFlaggedInCatalogSearch()
    {
        var catalog = new CabinetActionCatalog(index: CabinetCapabilities.Standard());

        var hits = catalog.Search("upload files", 8);

        var multipart = hits.Single(h => h.Path == "/store-storage/files" && h.Method == "PUT");
        Assert.False(multipart.CallableViaJson);
        Assert.Equal("multipart", multipart.Transport);
        var json = hits.Single(h => h.Path == "/store-storage/files/from-text");
        Assert.True(json.CallableViaJson);
    }
}
