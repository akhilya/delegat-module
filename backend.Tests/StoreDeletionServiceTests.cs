using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class StoreDeletionServiceTests
{
    private sealed class FakeStorage : IStorageService
    {
        public List<string> Objects { get; } = new();
        public List<string> Deleted { get; } = new();

        public string Bucket => "test";
        public Task<bool> UploadAsync(Microsoft.AspNetCore.Http.IFormFile file, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult<FileUploadResult?>(null);
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            Deleted.Add(path);
            Objects.Remove(path);
            return Task.FromResult(true);
        }
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(Objects.Contains(path));
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public string GetFullPath(string path) => path;
        public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult("");
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult("");
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) =>
            Task.FromResult(Objects.Where(o => o.StartsWith(prefix, StringComparison.Ordinal)).ToList());
    }

    private sealed class StubBranding : IBrandingSettingsService
    {
        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public BrandingSettingsDto GetDefaults() =>
            new("Delegat.Module", "", "/logo-square-favicon.webp", false, false, null, null, DateTime.UtcNow);
        public Task<BrandingSettingsDto> GetForStoreAsync(int storeId, CancellationToken ct = default) =>
            Task.FromResult(GetDefaults() with { storeId = storeId });
        public Task<BrandingSettingsDto> UpdateAsync(int storeId, UpdateBrandingSettingsRequest request, CancellationToken ct = default) =>
            GetForStoreAsync(storeId, ct);
        public Task<BrandingSettingsDto> UploadLogoAsync(int storeId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default) =>
            GetForStoreAsync(storeId, ct);
        public Task<BrandingSettingsDto> UploadFaviconAsync(int storeId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default) =>
            GetForStoreAsync(storeId, ct);
        public Task<(Stream Stream, string ContentType)?> OpenLogoStreamAsync(int storeId, CancellationToken ct = default) =>
            Task.FromResult<(Stream Stream, string ContentType)?>(null);
        public Task<(Stream Stream, string ContentType)?> OpenFaviconStreamAsync(int storeId, CancellationToken ct = default) =>
            Task.FromResult<(Stream Stream, string ContentType)?>(null);
        public Task DeleteForStoreAsync(int storeId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<int, string>> GetFaviconUrlsAsync(IEnumerable<int> storeIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(
                storeIds.Distinct().ToDictionary(id => id, _ => "/logo-square-favicon.webp"));
    }

    private static (StoreDeletionService svc, DataContext db, FakeStorage storage, TestDataBuilder data) Create()
    {
        var db = TestDbFactory.CreateDb();
        var storage = new FakeStorage();
        var svc = new StoreDeletionService(db, storage, new StubBranding(), NullLogger<StoreDeletionService>.Instance);
        return (svc, db, storage, new TestDataBuilder(db));
    }

    [Fact]
    public async Task DeleteStoreAsync_NameMismatch_ThrowsBadRequest()
    {
        var (svc, db, _, data) = Create();
        data.AddStore("Keep");
        var victim = data.AddStore("Victim");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.DeleteStoreAsync(victim.Id, "wrong-name"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("store_delete_name_mismatch", ex.Code);
        Assert.Equal(2, await db.stores.CountAsync());
    }

    [Fact]
    public async Task DeleteStoreAsync_LastStore_ThrowsBadRequest()
    {
        var (svc, db, _, data) = Create();
        var only = data.AddStore("Only");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.DeleteStoreAsync(only.Id, "Only"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("store_delete_last", ex.Code);
        Assert.Equal(1, await db.stores.CountAsync());
    }

    [Fact]
    public async Task DeleteStoreAsync_NotFound_Throws()
    {
        var (svc, db, _, data) = Create();
        data.AddStore("A");
        data.AddStore("B");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.DeleteStoreAsync(99999, "Nope"));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteStoreAsync_ClearsSelectedStoreId_AndRemovesStore()
    {
        var (svc, db, storage, data) = Create();
        var keep = data.AddStore("Keep");
        var victim = data.AddStore("Victim");
        var user = data.AddUser("alice");
        user.selectedStoreId = victim.Id;
        victim.members.Add(user);
        storage.Objects.Add($"{victim.Id}/store/file.bin");
        storage.Objects.Add($"{keep.Id}/store/other.bin");
        await db.SaveChangesAsync();

        await svc.DeleteStoreAsync(victim.Id, "Victim");

        Assert.Null(await db.Users.Where(u => u.Id == user.Id).Select(u => u.selectedStoreId).SingleAsync());
        Assert.False(await db.stores.AnyAsync(s => s.Id == victim.Id));
        Assert.True(await db.stores.AnyAsync(s => s.Id == keep.Id));
        Assert.Contains($"{victim.Id}/store/file.bin", storage.Deleted);
        Assert.Contains($"{keep.Id}/store/other.bin", storage.Objects);
    }

    [Fact]
    public async Task DeleteStoreAsync_RequiresExactName_IncludingWhitespaceTrimOnly()
    {
        var (svc, db, _, data) = Create();
        data.AddStore("Keep");
        var victim = data.AddStore("Exact Name");
        await db.SaveChangesAsync();

        await svc.DeleteStoreAsync(victim.Id, "  Exact Name  ");

        Assert.False(await db.stores.AnyAsync(s => s.Id == victim.Id));
    }
}
