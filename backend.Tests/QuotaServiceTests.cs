using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class QuotaServiceTests
{
    private static (DataContext db, QuotaService quota, TestDataBuilder data) Create()
    {
        var db = TestDbFactory.CreateDb();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var quota = new QuotaService(uow);
        return (db, quota, new TestDataBuilder(db));
    }

    private static IDisposable SetEnv(string name, string? value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvRestore(name, previous);
    }

    private sealed class EnvRestore(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }

    [Fact]
    public async Task EnsureProductQuota_NoLimit_Allows()
    {
        using var _ = SetEnv("MAX_PRODUCTS", null);
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.AddProduct(store.Id);
        await data.SaveAsync();

        await quota.EnsureProductQuotaAsync(store.Id);
    }

    [Fact]
    public async Task EnsureProductQuota_UnderLimit_Allows()
    {
        using var _ = SetEnv("MAX_PRODUCTS", "5");
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.AddProduct(store.Id);
        data.AddProduct(store.Id);
        await data.SaveAsync();

        await quota.EnsureProductQuotaAsync(store.Id);
    }

    [Fact]
    public async Task EnsureProductQuota_AtLimit_ThrowsConflict()
    {
        using var _ = SetEnv("MAX_PRODUCTS", "2");
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.AddProduct(store.Id);
        data.AddProduct(store.Id);
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => quota.EnsureProductQuotaAsync(store.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task EnsureConnectionQuota_AtLimit_ThrowsConflict()
    {
        using var _ = SetEnv("MAX_CONNECTIONS", "1");
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.AddConnection(store.Id);
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => quota.EnsureConnectionQuotaAsync(store.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task EnsureConnectionQuota_UnderLimit_Allows()
    {
        using var _ = SetEnv("MAX_CONNECTIONS", "3");
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.AddConnection(store.Id);
        await data.SaveAsync();

        await quota.EnsureConnectionQuotaAsync(store.Id);
    }

    [Fact]
    public async Task EnsureFileQuota_AtLimit_ThrowsConflict()
    {
        using var _ = SetEnv("MAX_FILES", "1");
        var (db, quota, data) = Create();
        var user = data.AddUser("author");
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        db.files.Add(new Models.File
        {
            Id = 1,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "products/1/a.jpg",
            length = 10,
            contentType = "image/jpeg",
            fileType = Enums.FileType.Image,
            uploadDateTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => quota.EnsureFileQuotaAsync(store.Id, 1));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task EnsureStorageQuota_AtLimit_ThrowsConflict()
    {
        using var _ = SetEnv("MAX_STORAGE_GB", "0.000000000000001");
        var (db, quota, data) = Create();
        var store = data.AddStore();
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => quota.EnsureStorageQuotaAsync(store.Id, additionalBytes: 1024));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task EnsureProductQuota_CountsOnlySameStore()
    {
        using var _ = SetEnv("MAX_PRODUCTS", "1");
        var (db, quota, data) = Create();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        data.AddProduct(b.Id);
        await data.SaveAsync();

        await quota.EnsureProductQuotaAsync(a.Id);
    }
}
