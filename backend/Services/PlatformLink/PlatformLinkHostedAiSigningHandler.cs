using System.Text.Json;
using Delegat.Infrastructure.Configuration;

namespace Delegat.Services.PlatformLink;

public sealed class PlatformLinkHostedAiSigningHandler(PlatformLinkOptions options) : DelegatingHandler
{
    private const int MaxRequestBytes = 1_048_576;
    private const int MaxErrorBytes = 16_384;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!options.IsEnabled || request.RequestUri is not { IsAbsoluteUri: true } uri
            || !IsPlatformAiUri(uri))
        {
            throw new PlatformHostedAiRequestException("hosted_ai_unavailable", StatusCodes.Status503ServiceUnavailable);
        }

        var body = request.Content == null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        if (body.Length > MaxRequestBytes)
            throw new PlatformHostedAiRequestException("hosted_ai_request_too_large", StatusCodes.Status413PayloadTooLarge);

        if (request.Content != null)
        {
            var originalContent = request.Content;
            var content = new ByteArrayContent(body);
            foreach (var header in originalContent.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Content = content;
            originalContent.Dispose();
        }

        request.RequestUri = AddRequestId(uri);
        request.Headers.Remove("Authorization");
        request.Headers.Remove("x-api-key");
        request.Headers.Remove(PlatformLinkHmac.HeaderInstanceId);
        request.Headers.Remove(PlatformLinkHmac.HeaderTimestamp);
        request.Headers.Remove(PlatformLinkHmac.HeaderSignature);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pathAndQuery = request.RequestUri.PathAndQuery;
        var bodyHash = PlatformLinkHmac.Sha256Hex(body);
        var canonical = PlatformLinkHmac.BuildCanonical(request.Method.Method, pathAndQuery, timestamp, bodyHash);
        var signature = PlatformLinkHmac.Sign(options.InstanceLinkSecret!, canonical);
        request.Headers.TryAddWithoutValidation(PlatformLinkHmac.HeaderInstanceId, options.InstanceId);
        request.Headers.TryAddWithoutValidation(PlatformLinkHmac.HeaderTimestamp, timestamp.ToString());
        request.Headers.TryAddWithoutValidation(PlatformLinkHmac.HeaderSignature, signature);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return response;

        try
        {
            var errorCode = await ReadHostedAiErrorCodeAsync(response, cancellationToken);
            throw new PlatformHostedAiRequestException(errorCode, (int)response.StatusCode);
        }
        finally
        {
            response.Dispose();
        }
    }

    private bool IsPlatformAiUri(Uri uri)
    {
        if (!Uri.TryCreate(options.ControlPlaneUrl, UriKind.Absolute, out var controlPlane))
            return false;

        return string.Equals(uri.GetLeftPart(UriPartial.Authority), controlPlane.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/platform/v1/ai/", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri AddRequestId(Uri uri)
    {
        var existing = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("request_id=", StringComparison.OrdinalIgnoreCase));
        var query = string.Join("&", existing.Append("request_id=" + Guid.NewGuid().ToString("D")));
        return new UriBuilder(uri) { Query = query }.Uri;
    }

    private static async Task<string> ReadHostedAiErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var output = new MemoryStream();
            var buffer = new byte[1024];
            while (output.Length < MaxErrorBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxErrorBytes - (int)output.Length)), ct);
                if (read == 0) break;
                output.Write(buffer, 0, read);
            }

            using var document = JsonDocument.Parse(output.ToArray());
            if (document.RootElement.TryGetProperty("code", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } code
                && code.StartsWith("hosted_ai_", StringComparison.Ordinal)
                && code.Length <= 64)
                return code;
        }
        catch (Exception ex) when (ex is JsonException or IOException or ObjectDisposedException)
        {
            // Provider and gateway diagnostics stay private; the caller receives a stable localized code.
        }

        return "hosted_ai_provider_unavailable";
    }
}
