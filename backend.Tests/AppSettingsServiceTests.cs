using Delegat.Infrastructure.Middleware;
using Delegat.Services;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class AppSettingsServiceTests
{
    [Fact]
    public async Task GetSettingsAsync_SeedsDefaults()
    {
        await using var db = TestDbFactory.CreateDb();
        var svc = new AppSettingsService(db);

        var list = await svc.GetSettingsAsync();

        Assert.Contains(list, s => s.key == AppSettingKeys.MaxProducts);
        Assert.Contains(list, s => s.key == AppSettingKeys.MaxConnections);
        Assert.True(list.All(s => s.isEditable));
    }

    [Fact]
    public async Task UpdateSettingAsync_PersistsPositiveInt()
    {
        await using var db = TestDbFactory.CreateDb();
        var svc = new AppSettingsService(db);
        await svc.GetSettingsAsync();

        var updated = await svc.UpdateSettingAsync(AppSettingKeys.MaxProducts, "50", userId: 1);

        Assert.Equal("50", updated.value);
        Assert.Equal(50, await svc.GetPositiveIntAsync(AppSettingKeys.MaxProducts));
    }

    [Fact]
    public async Task UpdateSettingAsync_UnknownKey_ThrowsNotFound()
    {
        await using var db = TestDbFactory.CreateDb();
        var svc = new AppSettingsService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.UpdateSettingAsync("NO_SUCH_KEY", "1", 1));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateSettingAsync_NonPositiveInt_ThrowsBadRequest()
    {
        await using var db = TestDbFactory.CreateDb();
        var svc = new AppSettingsService(db);
        await svc.GetSettingsAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.UpdateSettingAsync(AppSettingKeys.MaxProducts, "0", 1));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateSettingAsync_EmptyClearsValue()
    {
        await using var db = TestDbFactory.CreateDb();
        var svc = new AppSettingsService(db);
        await svc.GetSettingsAsync();
        await svc.UpdateSettingAsync(AppSettingKeys.MaxFiles, "10", 1);

        var cleared = await svc.UpdateSettingAsync(AppSettingKeys.MaxFiles, "  ", 1);
        Assert.Null(cleared.value);
    }

    [Fact]
    public async Task GetPositiveIntAsync_BlankUsesFallbackOrNull()
    {
        await using var db = TestDbFactory.CreateDb();
        var svc = new AppSettingsService(db);
        await svc.GetSettingsAsync();

        // Without env override, blank DB value → fallback from env (may be null)
        var value = await svc.GetPositiveIntAsync(AppSettingKeys.MaxProducts);
        Assert.True(value is null or > 0);
    }
}
