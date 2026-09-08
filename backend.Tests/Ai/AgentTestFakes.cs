using System.Security.Claims;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

internal sealed class GrantAllPermissions : IPermissionService
{
    public Task EnsureRolesSeededAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task MigrateLegacyAccessAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([
            Permissions.AiUse, Permissions.CatalogManage, Permissions.FilesManage,
            Permissions.MarketplaceManage, Permissions.OrdersManage,
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
