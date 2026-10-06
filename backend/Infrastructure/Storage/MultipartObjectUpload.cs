using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;

namespace Delegat.Infrastructure.Storage;

/// <summary>
/// Reads a multipart upload and stores each file as the bytes arrive.
/// Model binding would spool the whole body to disk first, then copy it again.
/// </summary>
internal static class MultipartObjectUpload
{
    public readonly record struct SavedObject(string FileName, string ContentType, string Path, long Length);

    public static async Task<List<SavedObject>> SaveAsync(
        HttpRequest request,
        IStorageService storage,
        Func<string, string> pathForFileName,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
            throw ApiException.BadRequest("Expected multipart/form-data");

        var boundary = request.GetMultipartBoundary();
        if (string.IsNullOrEmpty(boundary))
            throw ApiException.BadRequest("Missing boundary in content-type header");

        var saved = new List<SavedObject>();
        try
        {
            var reader = new MultipartReader(boundary, request.Body);
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(ct)) != null)
            {
                if (string.IsNullOrEmpty(section.ContentDisposition))
                    continue;

                var contentDisposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
                if (!contentDisposition.IsFileDisposition())
                    continue;

                var rawName = contentDisposition.FileName.HasValue
                    ? contentDisposition.FileName.Value
                    : null;
                var fileName = FileNameSafety.Sanitize(rawName);
                var contentType = string.IsNullOrWhiteSpace(section.ContentType)
                    ? "application/octet-stream"
                    : section.ContentType;
                var path = pathForFileName(fileName);
                var knownLength = ReadPartLength(section);
                var result = await storage.UploadStreamAsync(section.Body, path, contentType, knownLength, ct);
                if (result == null)
                    throw ApiException.InternalServerError($"Failed to upload file {fileName} to storage");

                saved.Add(new SavedObject(fileName, result.ContentType, path, result.Length));
            }

            return saved;
        }
        catch
        {
            foreach (var item in saved)
                await storage.DeleteAsync(item.Path, ct);
            throw;
        }
    }

    private static long? ReadPartLength(MultipartSection section)
    {
        if (section.Headers == null || !section.Headers.TryGetValue("Content-Length", out var lengthHeader))
            return null;
        return long.TryParse(lengthHeader.FirstOrDefault(), out var length) && length > 0
            ? length
            : null;
    }
}
