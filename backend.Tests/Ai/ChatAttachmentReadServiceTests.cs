using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Delegat.Infrastructure.Storage;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

public class ChatAttachmentReadServiceTests
{
    [Fact]
    public async Task ReadAsync_ReturnsBoundedWorksheetRowsAndPagination()
    {
        await using var db = TestDbFactory.CreateDb();
        var (user, store) = await SeedAttachmentAsync(db, "marking.xlsx", CreateWorkbook());
        var service = new ChatAttachmentReadService(db, new MemoryStorage(CreateWorkbook()));

        var (firstResult, firstSummary) = await service.ReadAsync(store.Id, user.Id, "chat-read-test", 71, null, 1, 2, 1, 0, 12_000, default);

        using var first = JsonDocument.Parse(JsonSerializer.Serialize(firstResult));
        Assert.Equal("Read 2 rows from marking.xlsx (Products)", firstSummary);
        Assert.Equal("Products", first.RootElement.GetProperty("selectedSheet").GetString());
        Assert.Equal("SKU", first.RootElement.GetProperty("rows")[0].GetProperty("values")[0].GetString());
        Assert.Equal("00123", first.RootElement.GetProperty("rows")[1].GetProperty("values")[0].GetString());
        Assert.Equal(3, first.RootElement.GetProperty("nextStartRow").GetInt32());

        var (secondResult, _) = await service.ReadAsync(store.Id, user.Id, "chat-read-test", 71, "Products", 3, 2, 1, 0, 12_000, default);
        using var second = JsonDocument.Parse(JsonSerializer.Serialize(secondResult));
        Assert.Equal("00234", second.RootElement.GetProperty("rows")[0].GetProperty("values")[0].GetString());
        Assert.False(second.RootElement.GetProperty("nextStartRow").ValueKind == JsonValueKind.Number);
    }

    [Fact]
    public async Task ReadAsync_RejectsOtherUsersExpiredFilesAndUnsupportedFormats()
    {
        await using var db = TestDbFactory.CreateDb();
        var (user, store) = await SeedAttachmentAsync(db, "notes.pdf", Encoding.UTF8.GetBytes("pdf"));
        db.aiChatAttachments.Add(new AiChatAttachment
        {
            Id = 72,
            userId = user.Id,
            storeId = store.Id,
            sessionId = "chat-read-test",
            fileName = "old.txt",
            contentType = "text/plain",
            length = 3,
            storageKey = "staged/old.txt",
            createdAt = DateTime.UtcNow - AgentFileUploadService.ChatAttachTtl - TimeSpan.FromMinutes(1),
        });
        await db.SaveChangesAsync();

        var service = new ChatAttachmentReadService(db, new MemoryStorage(Encoding.UTF8.GetBytes("pdf")));

        var (wrongOwner, _) = await service.ReadAsync(store.Id, user.Id + 1, "chat-read-test", 71, null, 1, 50, 1, 0, 12_000, default);
        var (wrongSession, _) = await service.ReadAsync(store.Id, user.Id, "another-chat", 71, null, 1, 50, 1, 0, 12_000, default);
        var (expired, _) = await service.ReadAsync(store.Id, user.Id, "chat-read-test", 72, null, 1, 50, 1, 0, 12_000, default);
        var (unsupported, _) = await service.ReadAsync(store.Id, user.Id, "chat-read-test", 71, null, 1, 50, 1, 0, 12_000, default);

        Assert.Contains("attachment_not_found_or_expired", JsonSerializer.Serialize(wrongOwner), StringComparison.Ordinal);
        Assert.Contains("attachment_not_found_or_expired", JsonSerializer.Serialize(wrongSession), StringComparison.Ordinal);
        Assert.Contains("attachment_not_found_or_expired", JsonSerializer.Serialize(expired), StringComparison.Ordinal);
        Assert.Contains("unsupported_attachment_format", JsonSerializer.Serialize(unsupported), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_RejectsOversizedFilesBeforeOpeningStorage()
    {
        await using var db = TestDbFactory.CreateDb();
        var (user, store) = await SeedAttachmentAsync(db, "large.txt", [1]);
        var attachment = db.aiChatAttachments.Single();
        attachment.length = ChatAttachmentReadService.MaxInputBytes + 1;
        await db.SaveChangesAsync();
        var storage = new MemoryStorage([1]);
        var service = new ChatAttachmentReadService(db, storage);

        var (result, _) = await service.ReadAsync(store.Id, user.Id, "chat-read-test", attachment.Id, null, 1, 50, 1, 0, 12_000, default);

        Assert.Contains("attachment_too_large", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Equal(0, storage.OpenCalls);
    }

    [Fact]
    public async Task ReadAsync_PaginatesTextWithoutSilentlyDroppingTheTail()
    {
        await using var db = TestDbFactory.CreateDb();
        var bytes = Encoding.UTF8.GetBytes("line-one\nline-two\nline-three");
        var (user, store) = await SeedAttachmentAsync(db, "notes.txt", bytes);
        var service = new ChatAttachmentReadService(db, new MemoryStorage(bytes));

        var (firstResult, _) = await service.ReadAsync(store.Id, user.Id, "chat-read-test", 71, null, 1, 50, 1, 0, 10, default);
        using var first = JsonDocument.Parse(JsonSerializer.Serialize(firstResult));
        Assert.Equal("line-one\nl", first.RootElement.GetProperty("text").GetString());
        Assert.Equal(10, first.RootElement.GetProperty("nextStartOffset").GetInt32());

        var (secondResult, _) = await service.ReadAsync(store.Id, user.Id, "chat-read-test", 71, null, 1, 50, 1, 10, 10, default);
        using var second = JsonDocument.Parse(JsonSerializer.Serialize(secondResult));
        Assert.Equal("ine-two\nli", second.RootElement.GetProperty("text").GetString());
        Assert.Equal(20, second.RootElement.GetProperty("nextStartOffset").GetInt32());
    }

    [Fact]
    public async Task ReadAsync_PaginatesWideWorksheetsAcrossColumns()
    {
        await using var db = TestDbFactory.CreateDb();
        var bytes = CreateWideWorkbook();
        var (user, store) = await SeedAttachmentAsync(db, "wide.xlsx", bytes);
        var service = new ChatAttachmentReadService(db, new MemoryStorage(bytes));

        var (firstResult, _) = await service.ReadAsync(store.Id, user.Id, "chat-read-test", 71, null, 1, 50, 1, 0, 12_000, default);
        using var first = JsonDocument.Parse(JsonSerializer.Serialize(firstResult));
        Assert.Equal(20, first.RootElement.GetProperty("columnsIncluded").GetInt32());
        Assert.Equal(21, first.RootElement.GetProperty("nextStartColumn").GetInt32());

        var (secondResult, _) = await service.ReadAsync(store.Id, user.Id, "chat-read-test", 71, null, 1, 50, 21, 0, 12_000, default);
        using var second = JsonDocument.Parse(JsonSerializer.Serialize(secondResult));
        Assert.Equal("Status", second.RootElement.GetProperty("rows")[0].GetProperty("values")[0].GetString());
        Assert.False(second.RootElement.GetProperty("nextStartColumn").ValueKind == JsonValueKind.Number);
    }

    private static byte[] CreateWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Products");
        sheet.Cell(1, 1).Value = "SKU";
        sheet.Cell(1, 2).Value = "Status";
        sheet.Cell(2, 1).Value = "00123";
        sheet.Cell(2, 2).Value = "Скоро скроем";
        sheet.Cell(3, 1).Value = "00234";
        sheet.Cell(3, 2).Value = "Активен";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateWideWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Wide");
        sheet.Cell(1, 1).Value = "SKU";
        sheet.Cell(1, 21).Value = "Status";
        sheet.Cell(2, 1).Value = "SKU-1";
        sheet.Cell(2, 21).Value = "Скоро скроем";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<(Delegat.Models.User User, Delegat.Models.Store Store)> SeedAttachmentAsync(
        DataContext db,
        string fileName,
        byte[] bytes)
    {
        var data = new TestDataBuilder(db);
        var user = data.AddUser("attachment-reader");
        var store = data.AddStore("attachment-reader-store", "RUB", "UTC", user);
        db.aiChatAttachments.Add(new AiChatAttachment
        {
            Id = 71,
            userId = user.Id,
            storeId = store.Id,
            sessionId = "chat-read-test",
            fileName = fileName,
            contentType = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/octet-stream",
            length = bytes.Length,
            storageKey = "staged/" + fileName,
            createdAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (user, store);
    }

    private sealed class MemoryStorage(byte[] bytes) : IStorageService
    {
        public int OpenCalls { get; private set; }
        public string Bucket => "test";
        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult<FileUploadResult?>(null);
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default)
        {
            OpenCalls++;
            return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
        }
        public string GetFullPath(string path) => path;
        public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => Task.FromResult(new List<string>());
    }
}
