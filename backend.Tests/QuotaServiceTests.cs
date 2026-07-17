using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class QuotaServiceTests
{
    private static (DataContext db, QuotaService quota, TestDataBuilder data) Create()
    {
        var db = TestDbFactory.CreateDb();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var settings = new AppSettingsService(db);
        var quota = new QuotaService(uow, settings);
        return (db, quota, new TestDataBuilder(db));
    }

    [Fact]
    public async Task EnsureProductQuota_NoLimit_Allows()
    {
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.AddProduct(store.Id);
        await data.SaveAsync();

        await quota.EnsureProductQuotaAsync(store.Id);
    }

    [Fact]
    public async Task EnsureProductQuota_UnderLimit_Allows()
    {
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.SetAppSetting(AppSettingKeys.MaxProducts, "5");
        data.AddProduct(store.Id);
        data.AddProduct(store.Id);
        await data.SaveAsync();

        await quota.EnsureProductQuotaAsync(store.Id);
    }

    [Fact]
    public async Task EnsureProductQuota_AtLimit_ThrowsConflict()
    {
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.SetAppSetting(AppSettingKeys.MaxProducts, "2");
        data.AddProduct(store.Id);
        data.AddProduct(store.Id);
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => quota.EnsureProductQuotaAsync(store.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task EnsureConnectionQuota_AtLimit_ThrowsConflict()
    {
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.SetAppSetting(AppSettingKeys.MaxConnections, "1");
        data.AddConnection(store.Id);
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => quota.EnsureConnectionQuotaAsync(store.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task EnsureConnectionQuota_UnderLimit_Allows()
    {
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.SetAppSetting(AppSettingKeys.MaxConnections, "3");
        data.AddConnection(store.Id);
        await data.SaveAsync();

        await quota.EnsureConnectionQuotaAsync(store.Id);
    }

    [Fact]
    public async Task EnsureFileQuota_AtLimit_ThrowsConflict()
    {
        var (db, quota, data) = Create();
        var user = data.AddUser("author");
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        data.SetAppSetting(AppSettingKeys.MaxFiles, "1");
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
        var (db, quota, data) = Create();
        var store = data.AddStore();
        data.SetAppSetting(AppSettingKeys.MaxStorageGb, "0.000000001", "decimal"); // ~1 byte
        await data.SaveAsync();

        // Force a tiny limit via bytes: set max storage so additionalBytes exceeds
        // Re-set with a clearer approach: use store file of 1 byte and max ~0 GB parsed
        // GetPositiveGigabytesAsBytes with very small GB
        data.SetAppSetting(AppSettingKeys.MaxStorageGb, "0.000000000000001", "decimal");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => quota.EnsureStorageQuotaAsync(store.Id, additionalBytes: 1024));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task EnsureProductQuota_CountsOnlySameStore()
    {
        var (db, quota, data) = Create();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        data.SetAppSetting(AppSettingKeys.MaxProducts, "1");
        data.AddProduct(b.Id);
        await data.SaveAsync();

        await quota.EnsureProductQuotaAsync(a.Id);
    }
}
