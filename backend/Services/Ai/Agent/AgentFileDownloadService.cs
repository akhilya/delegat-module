using System.Security.Cryptography;
using Delegat.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai.Agent;

public sealed class AgentFileDownloadService
{
    private readonly DataContext _db;
    private readonly IStorageService _storage;
    private readonly AgentFileDownloadTicketService _tickets;

    public AgentFileDownloadService(
        DataContext db,
        IStorageService storage,
        AgentFileDownloadTicketService tickets)
    {
        _db = db;
        _storage = storage;
        _tickets = tickets;
    }

    public async Task<AgentFileDownloadLink> CreateLinkAsync(
        int storeId,
        int userId,
        int fileId,
        CancellationToken ct)
    {
        var file = await GetFileAsync(storeId, fileId, ct)
            ?? throw new AgentFileDownloadException("file_not_found");
        if (!await _storage.ExistsAsync(file.FilePath, ct))
            throw new AgentFileDownloadException("file_not_found");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var hashingStream = new HashingWriteStream(hash);
        await _storage.StreamToAsync(file.FilePath, hashingStream, ct);
        if (hashingStream.BytesWritten != file.SizeBytes)
            throw new AgentFileDownloadException("file_size_mismatch");

        var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(AgentFileDownloadTicketService.LifetimeSeconds);
        var token = _tickets.Create(file.Id, storeId, userId, expiresAt);
        var downloadPath = $"/api/agent/files/{file.Id}/download?ticket={Uri.EscapeDataString(token)}";

        return new AgentFileDownloadLink(
            file.FileName,
            file.SizeBytes,
            sha256,
            expiresAt,
            downloadPath);
    }

    public Task<AgentFileDownloadRecord?> GetFileAsync(int storeId, int fileId, CancellationToken ct)
    {
        return _db.files
            .AsNoTracking()
            .Where(file => file.Id == fileId)
            .Join(
                _db.products.IgnoreQueryFilters().Where(product => product.storeId == storeId),
                file => file.productId,
                product => product.Id,
                (file, _) => new AgentFileDownloadRecord(
                    file.Id,
                    file.filePath,
                    file.displayName ?? Path.GetFileName(file.filePath),
                    file.contentType,
                    file.length))
            .FirstOrDefaultAsync(ct);
    }

    public bool TryValidateTicket(string? token, DateTimeOffset now, out AgentFileDownloadTicket ticket) =>
        _tickets.TryValidate(token, now, out ticket);

    public async Task<bool> ExistsInStorageAsync(AgentFileDownloadRecord file, CancellationToken ct) =>
        await _storage.ExistsAsync(file.FilePath, ct);

    public Task StreamAsync(AgentFileDownloadRecord file, Stream destination, CancellationToken ct) =>
        _storage.StreamToAsync(file.FilePath, destination, ct);

    public Task StreamRangeAsync(
        AgentFileDownloadRecord file,
        long offset,
        long length,
        Stream destination,
        CancellationToken ct) =>
        _storage.StreamRangeToAsync(file.FilePath, offset, length, destination, ct);

    private sealed class HashingWriteStream(IncrementalHash hash) : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            hash.AppendData(buffer, offset, count);
            BytesWritten += count;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer.Span);
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
    }
}

public sealed record AgentFileDownloadRecord(int Id, string FilePath, string FileName, string ContentType, long SizeBytes);

public sealed record AgentFileDownloadLink(
    string FileName,
    long SizeBytes,
    string Sha256,
    DateTimeOffset ExpiresAt,
    string DownloadUrl);

public sealed class AgentFileDownloadException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
