using System.Security.Cryptography;
using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Delegat.Tests;

[Collection("sandbox-env")]
public class AgentControllerFileDownloadTests
{
    [Fact]
    public async Task DownloadFile_StreamsRequestedRangeWithPrivateHeaders()
    {
        var originalAiEnabled = Environment.GetEnvironmentVariable("AI_ENABLED");
        Environment.SetEnvironmentVariable("AI_ENABLED", "true");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var (user, store, fileId) = await SeedPrivateFileAsync(db);
            var content = System.Text.Encoding.UTF8.GetBytes("0123456789");
            var storage = new MemoryStorage(content);
            var ticketService = CreateTicketService();
            var tickets = ticketService.Create(fileId, store.Id, user.Id, DateTimeOffset.UtcNow.AddMinutes(5));
            var downloads = new AgentFileDownloadService(db, storage, ticketService);
            var context = new DefaultHttpContext();
            context.Request.Headers["Range"] = "bytes=3-6";
            context.Response.Body = new MemoryStream();
            var controller = CreateController(db, context, downloads);

            var result = await controller.DownloadFile(fileId, tickets, default);

            Assert.IsType<EmptyResult>(result);
            Assert.Equal(StatusCodes.Status206PartialContent, context.Response.StatusCode);
            Assert.Equal("bytes 3-6/10", context.Response.Headers["Content-Range"]);
            Assert.Equal("bytes", context.Response.Headers["Accept-Ranges"]);
            Assert.Equal("private, no-store", context.Response.Headers["Cache-Control"]);
            Assert.Equal(4, context.Response.ContentLength);
            Assert.Equal("3456", System.Text.Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
            Assert.Equal(0, storage.FullStreamCalls);
            Assert.Equal(1, storage.RangeStreamCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", originalAiEnabled);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadFile_ReusesTicketAndRechecksUserForEveryRequest(bool deactivateBeforeLastRequest)
    {
        var originalAiEnabled = Environment.GetEnvironmentVariable("AI_ENABLED");
        Environment.SetEnvironmentVariable("AI_ENABLED", "true");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var (user, store, fileId) = await SeedPrivateFileAsync(db);
            var storage = new MemoryStorage(System.Text.Encoding.UTF8.GetBytes("0123456789"));
            var tickets = CreateTicketService();
            var ticket = tickets.Create(fileId, store.Id, user.Id, DateTimeOffset.UtcNow.AddMinutes(5));
            var downloads = new AgentFileDownloadService(db, storage, tickets);

            foreach (var (range, expected) in new[] { ("bytes=0-4", "01234"), ("bytes=5-9", "56789") })
            {
                var context = new DefaultHttpContext();
                context.Request.Headers["Range"] = range;
                context.Response.Body = new MemoryStream();
                var controller = CreateController(db, context, downloads);

                var result = await controller.DownloadFile(fileId, ticket, default);

                Assert.IsType<EmptyResult>(result);
                Assert.Equal(StatusCodes.Status206PartialContent, context.Response.StatusCode);
                Assert.Equal(expected,
                    System.Text.Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
            }

            if (deactivateBeforeLastRequest)
            {
                user.isActive = false;
                await db.SaveChangesAsync();
            }
            var lastContext = new DefaultHttpContext();
            lastContext.Response.Body = new MemoryStream();
            var lastController = CreateController(db, lastContext, downloads);

            var lastResult = await lastController.DownloadFile(fileId, ticket, default);

            if (deactivateBeforeLastRequest)
            {
                Assert.IsType<NotFoundResult>(lastResult);
                Assert.Equal(0, storage.FullStreamCalls);
                Assert.Equal(0, lastContext.Response.Body.Length);
            }
            else
            {
                Assert.IsType<EmptyResult>(lastResult);
                Assert.Equal(1, storage.FullStreamCalls);
                Assert.Equal("0123456789",
                    System.Text.Encoding.UTF8.GetString(((MemoryStream)lastContext.Response.Body).ToArray()));
            }
            Assert.Equal(2, storage.RangeStreamCalls);
            Assert.False((await db.files.FindAsync(fileId))!.isPublic);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", originalAiEnabled);
        }
    }

    [Fact]
    public async Task DownloadFile_RejectsExpiredAndCrossStoreTicketsBeforeStorageRead()
    {
        var originalAiEnabled = Environment.GetEnvironmentVariable("AI_ENABLED");
        Environment.SetEnvironmentVariable("AI_ENABLED", "true");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var (user, store, fileId) = await SeedPrivateFileAsync(db);
            var otherUser = new User
            {
                Id = 2,
                UserName = "unrelated-ticket-user",
                NormalizedUserName = "UNRELATED-TICKET-USER",
                Email = "unrelated-ticket-user@example.com",
                NormalizedEmail = "UNRELATED-TICKET-USER@EXAMPLE.COM",
                isActive = true,
                SecurityStamp = Guid.NewGuid().ToString(),
            };
            var otherStore = new Store { Id = 2, name = "unrelated-ticket-store", baseCurrency = "RUB", timezone = "UTC" };
            otherStore.members.Add(otherUser);
            db.Users.Add(otherUser);
            db.stores.Add(otherStore);
            await db.SaveChangesAsync();

            var storage = new MemoryStorage([1, 2, 3]);
            var ticketService = CreateTicketService();
            var downloads = new AgentFileDownloadService(db, storage, ticketService);
            var expired = ticketService.Create(fileId, store.Id, user.Id, DateTimeOffset.UtcNow.AddSeconds(-1));
            var forgedCrossStore = ticketService.Create(fileId, otherStore.Id, otherUser.Id, DateTimeOffset.UtcNow.AddMinutes(5));

            var expiredContext = new DefaultHttpContext();
            expiredContext.Response.Body = new MemoryStream();
            var expiredController = CreateController(db, expiredContext, downloads);
            var expiredResult = await expiredController.DownloadFile(fileId, expired, default);

            var crossStoreContext = new DefaultHttpContext();
            crossStoreContext.Response.Body = new MemoryStream();
            var crossStoreController = CreateController(db, crossStoreContext, downloads);
            var crossStoreResult = await crossStoreController.DownloadFile(fileId, forgedCrossStore, default);

            Assert.IsType<NotFoundResult>(expiredResult);
            Assert.IsType<NotFoundResult>(crossStoreResult);
            Assert.Equal(0, storage.FullStreamCalls);
            Assert.Equal(0, storage.RangeStreamCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", originalAiEnabled);
        }
    }

    [Fact]
    public async Task DownloadFile_PropagatesCancellationDuringBodyStream()
    {
        var originalAiEnabled = Environment.GetEnvironmentVariable("AI_ENABLED");
        Environment.SetEnvironmentVariable("AI_ENABLED", "true");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var (user, store, fileId) = await SeedPrivateFileAsync(db);
            using var cancellation = new CancellationTokenSource();
            var storage = new MemoryStorage(System.Text.Encoding.UTF8.GetBytes("0123456789"), cancellation);
            var ticketService = CreateTicketService();
            var ticket = ticketService.Create(fileId, store.Id, user.Id, DateTimeOffset.UtcNow.AddMinutes(5));
            var downloads = new AgentFileDownloadService(db, storage, ticketService);
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            var controller = CreateController(db, context, downloads);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => controller.DownloadFile(fileId, ticket, cancellation.Token));

            Assert.Equal(1, storage.FullStreamCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", originalAiEnabled);
        }
    }

    private static async Task<(User User, Store Store, int FileId)> SeedPrivateFileAsync(DataContext db)
    {
        var data = new TestDataBuilder(db);
        var user = data.AddUser("download-endpoint-user");
        var store = data.AddStore("download-endpoint-store", "RUB", "UTC", user);
        var product = data.AddProduct(store.Id, "Private file");
        const int fileId = 51;
        db.files.Add(new Delegat.Models.File
        {
            Id = fileId,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "private/test.stl",
            displayName = "test.stl",
            contentType = "model/stl",
            length = 10,
            isPublic = false,
        });
        await db.SaveChangesAsync();
        return (user, store, fileId);
    }

    private static AgentController CreateController(
        DataContext db,
        DefaultHttpContext context,
        AgentFileDownloadService downloads)
    {
        var sessions = new AgentSessionResolver(
            new FakeCurrentUserService(),
            new GrantAllPermissions(),
            db,
            new HttpContextAccessor { HttpContext = context });
        return new AgentController(
            sessions,
            null!,
            null!,
            null!,
            null!,
            null!,
            downloads)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private static AgentFileDownloadTicketService CreateTicketService() =>
        new(new JwtSettings { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });

    private sealed class MemoryStorage(byte[] content, CancellationTokenSource? cancelOnWrite = null) : IStorageService
    {
        public string Bucket => "test";
        public int FullStreamCalls { get; private set; }
        public int RangeStreamCalls { get; private set; }
        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult<FileUploadResult?>(null);
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => Task.FromResult<Stream?>(new MemoryStream(content, writable: false));
        public string GetFullPath(string path) => path;

        public async Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default)
        {
            FullStreamCalls++;
            var firstCount = content.Length / 2;
            await outputStream.WriteAsync(content.AsMemory(0, firstCount), ct);
            cancelOnWrite?.Cancel();
            await outputStream.WriteAsync(content.AsMemory(firstCount), ct);
        }

        public async Task StreamRangeToAsync(string path, long offset, long length, Stream outputStream, CancellationToken ct = default)
        {
            RangeStreamCalls++;
            await using var source = new MemoryStream(content, writable: false);
            source.Position = offset;
            await StorageRangeCopy.CopyExactlyAsync(source, outputStream, length, ct);
        }

        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => Task.FromResult(new List<string>());
    }
}
