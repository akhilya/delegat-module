using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Models.Marketplace;
using Delegat.Services;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Creates a marketplace connection only after the adapter confirms the credentials work.
/// </summary>
public sealed class MarketplaceConnectionCreateService
{
    private readonly IMarketplaceAdapterRegistry _adapters;
    private readonly IUnitOfWork _unitOfWork;
    private readonly QuotaService _quota;
    private readonly EncryptionService? _encryption;

    public MarketplaceConnectionCreateService(
        IMarketplaceAdapterRegistry adapters,
        IUnitOfWork unitOfWork,
        QuotaService quota,
        EncryptionService? encryption = null)
    {
        _adapters = adapters;
        _unitOfWork = unitOfWork;
        _quota = quota;
        _encryption = encryption;
    }

    public async Task<MarketplaceConnection> CreateAsync(
        MarketplaceConnection.UserAddForm input,
        int storeId,
        CancellationToken ct = default)
    {
        var adapterKey = (input.adapterKey ?? "").Trim();
        var adapter = _adapters.GetByKey(adapterKey)
            ?? throw ApiException.BadRequest($"Unknown adapter: {input.adapterKey}");

        await _quota.EnsureConnectionQuotaAsync(storeId);

        if (_encryption == null)
            throw ApiException.InternalServerError("Encryption is not configured");

        var encryptedCreds = !string.IsNullOrEmpty(input.credentialsJson)
            ? _encryption.Encrypt(input.credentialsJson)
            : input.credentialsJson;

        var connection = new MarketplaceConnection();
        connection.AddFill(new MarketplaceConnection.AddForm
        {
            name = input.name,
            adapterKey = adapterKey,
            credentialsJson = encryptedCreds,
            settingsJson = input.settingsJson,
            isActive = input.isActive,
            storeId = storeId,
        });

        Models.Marketplace.DTOs.ConnectionTestResult test;
        try
        {
            test = await adapter.TestConnectionAsync(connection, ct);
        }
        catch (Exception ex) when (ex is not ApiException)
        {
            throw ApiException.BadRequest(
                string.IsNullOrWhiteSpace(ex.Message) ? "Connection test failed" : ex.Message,
                code: "connection_test_failed");
        }

        if (!test.Success)
        {
            throw ApiException.BadRequest(
                string.IsNullOrWhiteSpace(test.Message) ? "Connection test failed" : test.Message,
                code: "connection_test_failed");
        }

        ConnectionCredentialsHelper.TryPersistMissingPublicFieldsFromDetails(
            connection,
            test.Details,
            adapter.GetCredentialsSchema(),
            _encryption,
            "businessId", "campaignId", "storeId");

        await EnsureUniqueRemoteShopAsync(
            storeId,
            adapterKey,
            connection.credentialsJson,
            test.Details,
            excludeConnectionId: null,
            ct);

        var repo = _unitOfWork.Repository<MarketplaceConnection>();
        await repo.AddAsync(connection, ct);

        return connection;
    }

    public async Task EnsureUniqueRemoteShopAsync(
        int storeId,
        string adapterKey,
        string? credentialsJson,
        IReadOnlyDictionary<string, object>? testDetails,
        int? excludeConnectionId,
        CancellationToken ct = default)
    {
        if (_encryption == null)
            throw ApiException.InternalServerError("Encryption is not configured");

        var incomingCreds = ConnectionCredentialsHelper.ParseCredentialsObject(
            ConnectionCredentialsHelper.DecryptCredentialsJson(credentialsJson, _encryption));
        var incoming = ConnectionRemoteIdentity.Build(adapterKey, incomingCreds, testDetails);

        var repo = _unitOfWork.Repository<MarketplaceConnection>();
        var key = adapterKey.ToLowerInvariant();
        List<MarketplaceConnection> siblings;
        if (excludeConnectionId is int excludeId)
        {
            siblings = await repo.FindAllAsync(
                c => c.storeId == storeId
                    && c.adapterKey.ToLower() == key
                    && c.Id != excludeId,
                ct);
        }
        else
        {
            siblings = await repo.FindAllAsync(
                c => c.storeId == storeId && c.adapterKey.ToLower() == key,
                ct);
        }

        foreach (var sibling in siblings)
        {
            var sibCreds = ConnectionCredentialsHelper.ParseCredentialsObject(
                ConnectionCredentialsHelper.DecryptCredentialsJson(sibling.credentialsJson, _encryption));
            var existing = ConnectionRemoteIdentity.Build(sibling.adapterKey, sibCreds);
            if (string.Equals(incoming, existing, StringComparison.Ordinal))
            {
                throw ApiException.Conflict(
                    "This marketplace shop is already connected in this store",
                    data: new { adapterKey, connectionId = sibling.Id },
                    code: "connection_remote_exists");
            }
        }
    }
}
