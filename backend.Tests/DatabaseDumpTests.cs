using Delegat.Controllers;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class DatabaseDumpTests
{
    [Fact]
    public async Task DownloadDatabaseDump_ReturnsFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"delegat-test-dump-{Guid.NewGuid():N}.sql");
        await File.WriteAllTextAsync(tempPath, "-- test dump\n");
        var released = false;
        DatabaseDumpHandle? handle = null;

        try
        {
            var dump = new FakeDumpService
            {
                Impl = () => Task.FromResult(new DatabaseDumpHandle(
                    tempPath,
                    "delegat-test.sql",
                    () => released = true)),
            };
            var controller = CreateController(dump);

            var result = await controller.DownloadDatabaseDump(CancellationToken.None);
            var file = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(tempPath, file.FileName);
            Assert.Equal("delegat-test.sql", file.FileDownloadName);
            Assert.Equal("application/sql", file.ContentType);

            handle = dump.LastHandle;
            Assert.NotNull(handle);
        }
        finally
        {
            if (handle != null)
                await handle.DisposeAsync();
            else if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        Assert.True(released);
    }

    [Fact]
    public async Task CreateDumpAsync_ThrowsUnavailable_WhenExecutableMissing()
    {
        var settings = new MySqlSettings
        {
            Server = "127.0.0.1",
            Port = 3306,
            Database = "delegat",
            User = "root",
            Password = "secret",
        };
        var service = new DatabaseDumpService(
            settings,
            NullLogger<DatabaseDumpService>.Instance,
            executable: Path.Combine(Path.GetTempPath(), $"missing-mysqldump-{Guid.NewGuid():N}"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CreateDumpAsync());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ex.StatusCode);
        Assert.Equal("mysqldump_unavailable", ex.Code);
    }

    [Theory]
    [InlineData("Access denied for user 'x'@'%' using password: secret", "secret", "Access denied for user 'x'@'%' using password: ***")]
    [InlineData("ok", null, "ok")]
    public void SanitizeStderr_RedactsPassword(string stderr, string? password, string expected)
    {
        Assert.Equal(expected, DatabaseDumpService.SanitizeStderr(stderr, password));
    }

    [Fact]
    public async Task DefaultsExtraFile_IsWrittenWithoutUtf8Bom()
    {
        Assert.Empty(DatabaseDumpService.DefaultsExtraFileEncoding.GetPreamble());

        var path = Path.Combine(Path.GetTempPath(), $"delegat-cnf-bom-{Guid.NewGuid():N}.cnf");
        try
        {
            await File.WriteAllTextAsync(
                path,
                "[client]\nhost=127.0.0.1\n",
                DatabaseDumpService.DefaultsExtraFileEncoding);

            await using var stream = File.OpenRead(path);
            var header = new byte[3];
            var read = await stream.ReadAsync(header);
            Assert.True(read >= 1);
            Assert.False(
                read >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF,
                "defaults-extra-file must not start with a UTF-8 BOM");
            Assert.Equal((byte)'[', header[0]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static SettingsController CreateController(IDatabaseDumpService dump)
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new DataContext(options);
        var quota = new QuotaService(new UnitOfWork(db), db);
        var entitlements = new PlatformEntitlementsService(db);
        var controller = new SettingsController(
            dump,
            new StubLoggingSettings(),
            new StubPublicApiSettings(),
            new StubBrandingSettings(),
            new StubCurrentUser(),
            quota,
            new StubOverageTrim(),
            entitlements);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    private sealed class StubOverageTrim : IOverageTrimService
    {
        public Task<OverageTrimPreviewDto> PreviewAsync(CancellationToken ct = default) =>
            Task.FromResult(new OverageTrimPreviewDto(
                false, null, false,
                Array.Empty<OverageTrimItemDto>(),
                Array.Empty<OverageTrimItemDto>(),
                Array.Empty<OverageTrimItemDto>(),
                0, 0, 0, 0));

        public Task<OverageTrimResultDto> ExecuteAsync(CancellationToken ct = default) =>
            Task.FromResult(new OverageTrimResultDto(0, 0, 0, 0));
    }

    private sealed class StubLoggingSettings : ILoggingSettingsService
    {
        public bool IsErrorLoggingEnabled => true;
        public bool IsApiRequestLoggingEnabled => false;
        public bool IsAdapterHttpLoggingEnabled => false;
        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<LoggingSettingsDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new LoggingSettingsDto(true, false, false, DateTime.UtcNow));
        public Task<LoggingSettingsDto> UpdateAsync(UpdateLoggingSettingsRequest request, CancellationToken ct = default) =>
            GetAsync(ct);
    }

    private sealed class StubPublicApiSettings : IPublicApiSettingsService
    {
        public bool IsPublicApiEnabled => true;
        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<PublicApiSettingsDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new PublicApiSettingsDto(true, DateTime.UtcNow));
        public Task<PublicApiSettingsDto> UpdateAsync(UpdatePublicApiSettingsRequest request, CancellationToken ct = default) =>
            GetAsync(ct);
    }

    private sealed class StubBrandingSettings : IBrandingSettingsService
    {
        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public BrandingSettingsDto GetDefaults() =>
            new("Delegat.Module", "", "/logo-square-favicon.webp", false, false, null, null, DateTime.UtcNow);
        public Task<BrandingSettingsDto> GetForStoreAsync(int storeId, CancellationToken ct = default) =>
            Task.FromResult(GetDefaults() with { storeId = storeId });
        public Task<BrandingSettingsDto> UpdateAsync(int storeId, UpdateBrandingSettingsRequest request, CancellationToken ct = default) =>
            GetForStoreAsync(storeId, ct);
        public Task<BrandingSettingsDto> UploadLogoAsync(int storeId, IFormFile file, CancellationToken ct = default) =>
            GetForStoreAsync(storeId, ct);
        public Task<BrandingSettingsDto> UploadFaviconAsync(int storeId, IFormFile file, CancellationToken ct = default) =>
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

    private sealed class StubCurrentUser : Delegat.Infrastructure.Auth.ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public int? GetUserId() => 1;
        public Task<Delegat.Models.User?> GetUserAsync(CancellationToken ct = default) =>
            Task.FromResult<Delegat.Models.User?>(null);
        public int? GetSelectedStoreId() => 1;
        public Task<int?> GetSelectedStoreIdAsync(CancellationToken ct = default) =>
            Task.FromResult<int?>(1);
    }

    private sealed class FakeDumpService : IDatabaseDumpService
    {
        public Func<Task<DatabaseDumpHandle>>? Impl { get; set; }
        public DatabaseDumpHandle? LastHandle { get; private set; }

        public async Task<DatabaseDumpHandle> CreateDumpAsync(CancellationToken cancellationToken = default)
        {
            LastHandle = await (Impl ?? (() => throw new InvalidOperationException("Impl not set")))();
            return LastHandle;
        }
    }
}
