using System.Reflection;
using System.Security.Claims;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Storage;
using Delegat.Services.Ai.Agent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Delegat.Tests;

internal sealed class GrantAllPermissions : IPermissionService
{
    public Task EnsureRolesSeededAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task MigrateLegacyAccessAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([
            Permissions.AiUse, Permissions.CatalogManage, Permissions.FilesManage,
            Permissions.MarketplaceManage, Permissions.OrdersManage, Permissions.OperationsManage,
        ]);
    public Task<IReadOnlyList<string>> GetRoleNamesAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
    public Task<UserAccessDto> GetUserAccessAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult(new UserAccessDto());
    public Task SetUserAccessAsync(int userId, IReadOnlyList<string> roleNames, IReadOnlyList<string> permissionGrants, CancellationToken ct = default) =>
        Task.CompletedTask;
    public Task EnsureOwnerAccessAsync(Delegat.Models.User user, CancellationToken ct = default) => Task.CompletedTask;
    public Task HydrateClaimsAsync(ClaimsIdentity identity, int userId, CancellationToken ct = default) =>
        Task.CompletedTask;
    public void InvalidateUserAccess(int userId) { }
}

internal sealed class FakeJwt : IJwtService
{
    public string GenerateToken(int userId, string userName) => "test-token";
    public ClaimsPrincipal? ValidateToken(string token) => null;
    public (string token, DateTime expiresAt) GenerateRefreshToken(bool rememberMe = false) =>
        ("r", DateTime.UtcNow.AddHours(1));
    public ClaimsPrincipal? ValidateExpiredToken(string token) => null;
}

internal sealed class ThrowingHttpFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

/// <summary>
/// Builds MVC action descriptors for the capability index without a live app:
/// enough metadata for route/method extraction plus multipart classification.
/// </summary>
internal static class CabinetActions
{
    public static IActionDescriptorCollectionProvider Provider(params ControllerActionDescriptor[] actions) =>
        new StubProvider(actions);

    public static ControllerActionDescriptor Action(
        Type controller,
        string actionName,
        string template,
        string method,
        bool multipartForm = false,
        Type? parameterType = null)
    {
        var controllerName = controller.Name;
        if (controllerName.EndsWith("Controller", StringComparison.Ordinal))
            controllerName = controllerName[..^"Controller".Length];
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = controllerName,
            ActionName = actionName,
            ControllerTypeInfo = controller.GetTypeInfo(),
            AttributeRouteInfo = new AttributeRouteInfo { Template = template },
            ActionConstraints = [new HttpMethodActionConstraint([method])],
            EndpointMetadata = [],
            FilterDescriptors = [],
            Parameters = [],
        };
        if (multipartForm)
        {
            descriptor.FilterDescriptors.Add(new FilterDescriptor(
                new DisableFormValueModelBindingAttribute(), FilterScope.Action));
        }
        if (parameterType != null)
        {
            descriptor.Parameters.Add(new ControllerParameterDescriptor
            {
                Name = "file",
                ParameterType = parameterType,
            });
        }
        return descriptor;
    }

    private sealed class StubProvider(ControllerActionDescriptor[] actions) : IActionDescriptorCollectionProvider
    {
        public ActionDescriptorCollection ActionDescriptors { get; } = new(actions, 0);
    }
}

internal sealed class FakeCabinetController : Microsoft.AspNetCore.Mvc.ControllerBase { }

/// <summary>A small representative route set: JSON routes, multipart uploads, a binary download.</summary>
internal static class CabinetCapabilities
{
    public static CabinetCapabilityIndex Standard() => new(CabinetActions.Provider(
        CabinetActions.Action(typeof(FakeCabinetController), "ListProducts", "/products", "GET"),
        CabinetActions.Action(typeof(FakeCabinetController), "CreateProduct", "/products", "POST"),
        CabinetActions.Action(typeof(FakeCabinetController), "GetProduct", "/products/{id}", "GET"),
        CabinetActions.Action(typeof(FakeCabinetController), "UploadStoreFiles", "/store-storage/files", "PUT", multipartForm: true),
        CabinetActions.Action(typeof(FakeCabinetController), "StoreFileFromText", "/store-storage/files/from-text", "POST"),
        CabinetActions.Action(typeof(FakeCabinetController), "UploadProductFiles", "/products/{productId}/files", "PUT", multipartForm: true),
        CabinetActions.Action(typeof(FakeCabinetController), "DownloadFile", "/files/{id}/download", "GET"),
        CabinetActions.Action(typeof(FakeCabinetController), "ImportPreview", "/products/import-preview", "POST", parameterType: typeof(IFormFile))));
}

internal sealed class FakeStorage : IStorageService
{
    public string Bucket => "test";
    public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult(true);
    public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) =>
        Task.FromResult<FileUploadResult?>(new FileUploadResult("text/plain", 1));
    public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> DeleteAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
    public string GetFullPath(string path) => path;
    public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
    public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
    public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult(new List<string>());
}
