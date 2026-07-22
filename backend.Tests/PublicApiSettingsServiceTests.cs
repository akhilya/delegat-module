using Delegat.Infrastructure.Configuration;
using Delegat.Models;
using Delegat.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class PublicApiSettingsServiceTests
{
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

    private sealed class RecordingLoggingSettings : ILoggingSettingsService
    {
        public UpdateLoggingSettingsRequest? LastUpdate { get; private set; }

        public bool IsErrorLoggingEnabled => true;
        public bool IsApiRequestLoggingEnabled => true;
        public bool IsAdapterHttpLoggingEnabled => false;
        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<LoggingSettingsDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new LoggingSettingsDto(true, true, false, DateTime.UtcNow));
        public Task<LoggingSettingsDto> UpdateAsync(UpdateLoggingSettingsRequest request, CancellationToken ct = default)
        {
            LastUpdate = request;
            return GetAsync(ct);
        }
    }

    private static (PublicApiSettingsService Service, DataContext Db, RecordingLoggingSettings Logging) CreateService(
        string? dbName = null)
    {
        var name = dbName ?? Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<DataContext>(o => o.UseInMemoryDatabase(name));
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logging = new RecordingLoggingSettings();
        var service = new PublicApiSettingsService(
            scopeFactory,
            logging,
            NullLogger<PublicApiSettingsService>.Instance);
        return (service, provider.GetRequiredService<DataContext>(), logging);
    }

    [Fact]
    public async Task GetAsync_SeedsSingletonFromEnv_WhenMissing()
    {
        using var _ = SetEnv("PUBLIC_API_ENABLED", "false");
        var (service, db, _) = CreateService();

        var dto = await service.GetAsync();

        Assert.False(dto.publicApiEnabled);
        Assert.False(service.IsPublicApiEnabled);
        var row = await db.instancePublicApiSettings.SingleAsync();
        Assert.Equal(InstancePublicApiSettings.SingletonId, row.Id);
        Assert.False(row.publicApiEnabled);
    }

    [Fact]
    public async Task GetAsync_UsesExistingRow_NotEnv()
    {
        using var _ = SetEnv("PUBLIC_API_ENABLED", "true");
        var (service, db, _) = CreateService();
        db.instancePublicApiSettings.Add(new InstancePublicApiSettings
        {
            Id = InstancePublicApiSettings.SingletonId,
            publicApiEnabled = false,
            updatedAt = DateTime.UtcNow.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var dto = await service.GetAsync();

        Assert.False(dto.publicApiEnabled);
        Assert.False(service.IsPublicApiEnabled);
    }

    [Fact]
    public async Task UpdateAsync_Disabling_TurnsOffApiRequestLogging()
    {
        var (service, _, logging) = CreateService();
        await service.GetAsync();

        var dto = await service.UpdateAsync(new UpdatePublicApiSettingsRequest { publicApiEnabled = false });

        Assert.False(dto.publicApiEnabled);
        Assert.False(service.IsPublicApiEnabled);
        Assert.NotNull(logging.LastUpdate);
        Assert.False(logging.LastUpdate!.logApiRequestsEnabled);
    }

    [Fact]
    public async Task UpdateAsync_Enabling_DoesNotTouchLogging()
    {
        var (service, db, logging) = CreateService();
        db.instancePublicApiSettings.Add(new InstancePublicApiSettings
        {
            Id = InstancePublicApiSettings.SingletonId,
            publicApiEnabled = false,
            updatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await service.GetAsync();

        var dto = await service.UpdateAsync(new UpdatePublicApiSettingsRequest { publicApiEnabled = true });

        Assert.True(dto.publicApiEnabled);
        Assert.Null(logging.LastUpdate);
    }

    [Fact]
    public async Task GetAsync_AfterInit_ReturnsCachedValueWithoutReseed()
    {
        var (service, db, _) = CreateService();
        await service.GetAsync();

        db.instancePublicApiSettings.RemoveRange(db.instancePublicApiSettings);
        await db.SaveChangesAsync();

        var dto = await service.GetAsync();
        Assert.True(dto.publicApiEnabled);
        Assert.Empty(db.instancePublicApiSettings);
    }
}
