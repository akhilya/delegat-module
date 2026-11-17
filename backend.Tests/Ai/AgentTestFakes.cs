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
    public Task EnsureOwnerAccessAsync(Models.User user, CancellationToken ct = default) => Task.CompletedTask;
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

/// <summary>Real in-memory object store: generated-artifact tests need actual
/// staged bytes (hash re-verification), so FakeStorage's no-op reads do not fit.</summary>
internal sealed class MemoryStorage : IStorageService
{
    public string Bucket => "test";
    public int UploadCount { get; private set; }
    public Dictionary<string, byte[]> StoredObjects { get; } = new(StringComparer.Ordinal);

    public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public async Task<bool> UploadAsync(
        Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default)
    {
        UploadCount++;
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, ct);
        StoredObjects[path] = output.ToArray();
        return true;
    }
    public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(
        string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
    {
        StoredObjects.Remove(path);
        return Task.FromResult(true);
    }
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(StoredObjects.ContainsKey(path));
    public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(
            StoredObjects.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null);
    public string GetFullPath(string path) => path;
    public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) =>
        outputStream.WriteAsync(StoredObjects[path], 0, StoredObjects[path].Length, ct);
    public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) =>
        Task.FromResult(path);
    public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) =>
        Task.FromResult(path);
    public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult(new List<string>());
}

/// <summary>Sandbox stub for artifact capture: /health advertises protocol 1 and
/// /run-artifacts answers a real ZIP archive containing manifest.json + outputs/.</summary>
internal sealed class ArtifactPythonHttp : HttpMessageHandler, IHttpClientFactory
{
    public byte[] Zip { get; }
    public int Posts { get; private set; }
    public int HealthProbes { get; private set; }
    public List<string> Paths { get; } = [];

    public ArtifactPythonHttp(
        IReadOnlyList<(string FileName, string ContentType, byte[] Bytes)> outputs,
        string stdout = "done")
    {
        Zip = Build(outputs, stdout);
    }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Add(request.Method + " " + path);
        if (request.Method == HttpMethod.Get)
        {
            HealthProbes++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"ok":true,"artifactProtocol":1}""",
                    System.Text.Encoding.UTF8, "application/json"),
            });
        }
        Posts++;
        if (path != "/run-artifacts")
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Zip)
            {
                Headers =
                {
                    ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip"),
                },
            },
        });
    }

    private static byte[] Build(
        IReadOnlyList<(string FileName, string ContentType, byte[] Bytes)> outputs, string stdout)
    {
        using var mem = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
                   mem, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var metas = new List<object>();
            for (var i = 0; i < outputs.Count; i++)
            {
                var entry = zip.CreateEntry($"outputs/{i}", System.IO.Compression.CompressionLevel.NoCompression);
                using var writer = entry.Open();
                writer.Write(outputs[i].Bytes);
                metas.Add(new
                {
                    index = i,
                    fileName = outputs[i].FileName,
                    contentType = outputs[i].ContentType,
                    length = outputs[i].Bytes.Length,
                    sha256 = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(outputs[i].Bytes)).ToLowerInvariant(),
                });
            }
            var manifest = System.Text.Json.JsonSerializer.Serialize(new
            {
                protocol = 1,
                result = new { stdout, stderr = "", exitCode = 0, error = (string?)null },
                outputs = metas,
            });
            var manifestEntry = zip.CreateEntry("manifest.json", System.IO.Compression.CompressionLevel.NoCompression);
            using var ms = manifestEntry.Open();
            ms.Write(System.Text.Encoding.UTF8.GetBytes(manifest));
        }
        return mem.ToArray();
    }
}

/// <summary>Static permission grant for permission-matrix tests.</summary>
internal sealed class StaticPermissions(params string[] permissions) : IPermissionService
{
    private readonly string[] _permissions = permissions;
    public Task EnsureRolesSeededAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task MigrateLegacyAccessAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(_permissions);
    public Task<IReadOnlyList<string>> GetRoleNamesAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
    public Task<UserAccessDto> GetUserAccessAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult(new UserAccessDto());
    public Task SetUserAccessAsync(int userId, IReadOnlyList<string> roleNames, IReadOnlyList<string> permissionGrants, CancellationToken ct = default) =>
        Task.CompletedTask;
    public Task EnsureOwnerAccessAsync(Models.User user, CancellationToken ct = default) => Task.CompletedTask;
    public Task HydrateClaimsAsync(ClaimsIdentity identity, int userId, CancellationToken ct = default) =>
        Task.CompletedTask;
    public void InvalidateUserAccess(int userId) { }
}
