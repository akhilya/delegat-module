using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Delegat.Services.Ai.Agent;

public sealed class AgentFileDownloadTicketService
{
    public const string Purpose = "Delegat.AgentFileDownload.v1";
    public const int LifetimeSeconds = 10 * 60;

    private readonly byte[] _key;

    public AgentFileDownloadTicketService(JwtSettings settings)
    {
        _key = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(settings.Key),
            Encoding.UTF8.GetBytes(Purpose));
    }

    public string Create(int fileId, int storeId, int userId, DateTimeOffset expiresAt)
    {
        if (fileId <= 0 || storeId <= 0 || userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(fileId));

        var payload = JsonSerializer.SerializeToUtf8Bytes(new TicketPayload(
            Purpose,
            fileId,
            storeId,
            userId,
            expiresAt.ToUnixTimeSeconds()));
        var encodedPayload = Base64UrlEncoder.Encode(payload);
        var signature = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(encodedPayload));
        return $"{encodedPayload}.{Base64UrlEncoder.Encode(signature)}";
    }

    public bool TryValidate(string? token, DateTimeOffset now, out AgentFileDownloadTicket ticket)
    {
        ticket = default;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            return false;

        var separator = token.IndexOf('.');
        if (separator <= 0 || separator != token.LastIndexOf('.') || separator == token.Length - 1)
            return false;

        try
        {
            var encodedPayload = token[..separator];
            var suppliedSignature = Base64UrlEncoder.DecodeBytes(token[(separator + 1)..]);
            var expectedSignature = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(encodedPayload));
            if (suppliedSignature.Length != expectedSignature.Length
                || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
                return false;

            var payload = JsonSerializer.Deserialize<TicketPayload>(Base64UrlEncoder.DecodeBytes(encodedPayload));
            if (payload == null
                || payload.Purpose != Purpose
                || payload.FileId <= 0
                || payload.StoreId <= 0
                || payload.UserId <= 0
                || payload.ExpiresUnix <= now.ToUnixTimeSeconds())
                return false;

            ticket = new AgentFileDownloadTicket(
                payload.FileId,
                payload.StoreId,
                payload.UserId,
                DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresUnix));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private sealed record TicketPayload(string Purpose, int FileId, int StoreId, int UserId, long ExpiresUnix);
}

public readonly record struct AgentFileDownloadTicket(
    int FileId,
    int StoreId,
    int UserId,
    DateTimeOffset ExpiresAt);
