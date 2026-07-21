using Delegat.Controllers;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class DatabaseDumpTests
{
    [Fact]
    public async Task DownloadDatabaseDump_Forbidden_WhenNotAdmin()
    {
        var user = new Models.User { Id = 1, UserName = "user", isActive = true, isAdmin = false };
        var current = new FakeCurrentUserService { UserId = user.Id, User = user };
        var controller = CreateController(current, new FakeDumpService());

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => controller.DownloadDatabaseDump(CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task DownloadDatabaseDump_Forbidden_WhenNoUser()
    {
        var current = new FakeCurrentUserService { UserId = null };
        var controller = CreateController(current, new FakeDumpService());

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => controller.DownloadDatabaseDump(CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task DownloadDatabaseDump_ReturnsFile_WhenAdmin()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"delegat-test-dump-{Guid.NewGuid():N}.sql");
        await File.WriteAllTextAsync(tempPath, "-- test dump\n");
        var released = false;
        DatabaseDumpHandle? handle = null;

        try
        {
            var user = new Models.User { Id = 1, UserName = "admin", isActive = true, isAdmin = true };
            var current = new FakeCurrentUserService { UserId = user.Id, User = user };
            var dump = new FakeDumpService
            {
                Impl = () => Task.FromResult(new DatabaseDumpHandle(
                    tempPath,
                    "delegat-test.sql",
                    () => released = true)),
            };
            var controller = CreateController(current, dump);

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

    private static SettingsController CreateController(
        FakeCurrentUserService current,
        IDatabaseDumpService dump)
    {
        var controller = new SettingsController(current, dump);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
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
