using System.Security.Cryptography;
using System.Text;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

public class AgentFileDownloadServiceTests
{
    [Fact]
    public async Task CreateLink_ReturnsMatchingNameSizeSha256AndShortExpiry()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("file-link-user");
        var store = data.AddStore("file-link-store", "RUB", "UTC", user);
        var product = data.AddProduct(store.Id, "Mesh");
        var bytes = Encoding.UTF8.GetBytes("solid sample mesh\nendsolid sample mesh\n");
        db.files.Add(new Delegat.Models.File
        {
            Id = 41,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "private/mesh.stl",
            displayName = "mesh.stl",
            contentType = "model/stl",
            length = bytes.Length,
            isPublic = false,
        });
        await db.SaveChangesAsync();

        var storage = new MemoryStorage(bytes);
        var tickets = CreateTicketService();
        var service = new AgentFileDownloadService(db, storage, tickets);
        var before = DateTimeOffset.UtcNow;
        var link = await service.CreateLinkAsync(store.Id, user.Id, 41, default);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal("mesh.stl", link.FileName);
        Assert.Equal(bytes.Length, link.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), link.Sha256);
        Assert.InRange(link.ExpiresAt, before.AddSeconds(AgentFileDownloadTicketService.LifetimeSeconds), after.AddSeconds(AgentFileDownloadTicketService.LifetimeSeconds));
        Assert.StartsWith("/api/agent/files/41/download?ticket=", link.DownloadUrl, StringComparison.Ordinal);
        Assert.True(tickets.TryValidate(ReadTicket(link.DownloadUrl), DateTimeOffset.UtcNow, out var grant));
        Assert.Equal(store.Id, grant.StoreId);
        Assert.Equal(user.Id, grant.UserId);
        Assert.False((await db.files.FindAsync(41))!.isPublic);
    }

    [Fact]
    public async Task CreateLink_DoesNotExposeFilesFromAnotherStore()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var owner = data.AddUser("other-store-owner");
        var authorizedUser = data.AddUser("link-caller");
        var ownerStore = data.AddStore("owner-store", "RUB", "UTC", owner);
        var callerStore = data.AddStore("caller-store", "RUB", "UTC", authorizedUser);
        var product = data.AddProduct(ownerStore.Id, "Private mesh");
        db.files.Add(new Delegat.Models.File
        {
            Id = 42,
            productId = product.Id,
            authorUserId = owner.Id,
            filePath = "private/other.stl",
            contentType = "model/stl",
            length = 4,
        });
        await db.SaveChangesAsync();

        var service = new AgentFileDownloadService(db, new MemoryStorage([1, 2, 3, 4]), CreateTicketService());
        var exception = await Assert.ThrowsAsync<AgentFileDownloadException>(
            () => service.CreateLinkAsync(callerStore.Id, authorizedUser.Id, 42, default));

        Assert.Equal("file_not_found", exception.Code);

        var sessions = new AgentSessionResolver(
            new FakeCurrentUserService(),
            new GrantAllPermissions(),
            db,
            new HttpContextAccessor());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sessions.ResolveForUserAsync(authorizedUser.Id, ownerStore.Id, default));
    }

    [Fact]
    public async Task CreateLink_PropagatesCancellationDuringStreamingHash()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("cancel-link-user");
        var store = data.AddStore("cancel-link-store", "RUB", "UTC", user);
        var product = data.AddProduct(store.Id);
        db.files.Add(new Delegat.Models.File
        {
            Id = 43,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "private/cancel.stl",
            contentType = "model/stl",
            length = 1,
        });
        await db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new AgentFileDownloadService(db, new MemoryStorage([1]), CreateTicketService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CreateLinkAsync(store.Id, user.Id, 43, cts.Token));
    }

    [Fact]
    public void ExpiredTicket_IsRejectedWithoutPersistentLinkCleanup()
    {
        var tickets = CreateTicketService();
        var now = DateTimeOffset.UtcNow;
        var token = tickets.Create(7, 2, 3, now.AddSeconds(1));

        Assert.True(tickets.TryValidate(token, now, out _));
        Assert.False(tickets.TryValidate(token, now.AddSeconds(2), out _));
        Assert.False(tickets.TryValidate(token + "x", now, out _));
    }

    [Fact]
    public void RepeatedTicketValidation_DoesNotConsumeOrExtendItsLifetime()
    {
        var tickets = CreateTicketService();
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var expiresAt = issuedAt.AddSeconds(AgentFileDownloadTicketService.LifetimeSeconds);
        var token = tickets.Create(7, 2, 3, expiresAt);

        foreach (var at in new[] { issuedAt, issuedAt, expiresAt.AddSeconds(-1) })
        {
            Assert.True(tickets.TryValidate(token, at, out var grant));
            Assert.Equal(expiresAt, grant.ExpiresAt);
        }
        Assert.False(tickets.TryValidate(token, expiresAt, out _));
    }

    [Theory]
    [InlineData("bytes=2-5", 8, 2, 5)]
    [InlineData("bytes=4-", 8, 4, 7)]
    [InlineData("bytes=-3", 8, 5, 7)]
    [InlineData("bytes=0-99", 8, 0, 7)]
    public void FileRangeParser_SupportsSingleRangeAndSuffixResume(
        string header,
        long size,
        long expectedStart,
        long expectedEnd)
    {
        Assert.True(FileByteRangeParser.TryParse(header, size, out var range));
        Assert.Equal(new FileByteRange(expectedStart, expectedEnd), range);
    }

    [Theory]
    [InlineData("bytes=8-", 8)]
    [InlineData("bytes=5-2", 8)]
    [InlineData("bytes=0-1,4-5", 8)]
    [InlineData("bytes=0-0", 0)]
    public void FileRangeParser_RejectsUnsatisfiableAndMultipleRanges(string header, long size)
    {
        Assert.False(FileByteRangeParser.TryParse(header, size, out _));
    }

    [Fact]
    public async Task StorageRangeStreaming_ReturnsOnlyRequestedBytesAndHonorsCancellation()
    {
        IStorageService storage = new MemoryStorage(Encoding.UTF8.GetBytes("0123456789"));
        await using var destination = new MemoryStream();
        await storage.StreamRangeToAsync("private/test.bin", 3, 4, destination, default);
        Assert.Equal("3456", Encoding.UTF8.GetString(destination.ToArray()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await storage.StreamRangeToAsync("private/test.bin", 0, 4, Stream.Null, cts.Token));
    }

    private static AgentFileDownloadTicketService CreateTicketService() =>
        new(new JwtSettings { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });

    private static string ReadTicket(string downloadUrl) =>
        Uri.UnescapeDataString(downloadUrl[(downloadUrl.IndexOf("ticket=", StringComparison.Ordinal) + "ticket=".Length)..]);

    private sealed class MemoryStorage(byte[] content) : IStorageService
    {
        public string Bucket => "test";
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
            const int chunkSize = 4096;
            for (var offset = 0; offset < content.Length; offset += chunkSize)
            {
                ct.ThrowIfCancellationRequested();
                var count = Math.Min(chunkSize, content.Length - offset);
                await outputStream.WriteAsync(content.AsMemory(offset, count), ct);
            }
        }
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => Task.FromResult(new List<string>());
    }
}
