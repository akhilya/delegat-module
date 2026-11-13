using Delegat.Infrastructure.Storage;
using Delegat.Services;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Reflection;

namespace Delegat.Tests.Infrastructure;

internal sealed class RecordingCatalogStorage : IStorageService
{
    public string Bucket => "isolated-catalog-tests";
    public ConcurrentBag<string> Objects { get; } = [];
    public ConcurrentBag<string> Deleted { get; } = [];
    public Action? OnCopy { get; set; }
    public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
    {
        OnCopy?.Invoke();
        ct.ThrowIfCancellationRequested();
        Objects.Add(destinationPath);
        return Task.FromResult(true);
    }
    public Task<bool> DeleteAsync(string path, CancellationToken ct = default) { Deleted.Add(path); return Task.FromResult(true); }
    public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult(Objects.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).ToList());
    public async Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default)
    {
        await Task.Delay(2, ct); // Deliberately fake network/storage; no external HTTP is sent.
        Objects.Add(path);
        return new FileUploadResult("image/jpeg", 64);
    }
    public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(Objects.Contains(path));
    public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => throw new NotSupportedException();
    public string GetFullPath(string path) => path;
    public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotSupportedException();
    public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotSupportedException();
}

public class CatalogBrandingProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "DeleteForStoreAsync"
        ? Task.CompletedTask : throw new NotSupportedException(method.Name);
}
