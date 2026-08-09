using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.YandexMarket;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class MarketplaceExternalIdBindTests
{
    [Fact]
    public void Ozon_ResolveExportOfferId_Linked_PrefersRemoteOfferId()
    {
        var offerId = OzonProductIdentity.ResolveExportOfferId(
            mappedSkuOrOfferId: "LOCAL-SKU",
            linkedCardOfferId: "REMOTE-OFFER",
            hasExistingExternalId: true);

        Assert.Equal("REMOTE-OFFER", offerId);
    }

    [Fact]
    public void Ozon_ResolveExportOfferId_Unlinked_UsesMappedSku()
    {
        var offerId = OzonProductIdentity.ResolveExportOfferId(
            mappedSkuOrOfferId: "LOCAL-SKU",
            linkedCardOfferId: "REMOTE-OFFER",
            hasExistingExternalId: false);

        Assert.Equal("LOCAL-SKU", offerId);
    }

    [Fact]
    public void Host_MayUpdateExternalProductId_SameId_Allows()
    {
        Assert.True(MarketplaceExportIdentity.MayUpdateExternalProductId(
            previousLinkExternalId: "111",
            pinnedExternalIdPassedToAdapter: "111",
            resultExternalId: "111",
            resolvedFrom: null));
    }

    [Fact]
    public void Host_MayUpdateExternalProductId_SilentRebind_Blocks()
    {
        Assert.False(MarketplaceExportIdentity.MayUpdateExternalProductId(
            previousLinkExternalId: "NEW-ID",
            pinnedExternalIdPassedToAdapter: "NEW-ID",
            resultExternalId: "OLD-ID",
            resolvedFrom: null));
    }

    [Fact]
    public void Host_MayUpdateExternalProductId_NormalizeFromSku_Allows()
    {
        Assert.True(MarketplaceExportIdentity.MayUpdateExternalProductId(
            previousLinkExternalId: "OZON-SKU-99",
            pinnedExternalIdPassedToAdapter: "OZON-SKU-99",
            resultExternalId: "555",
            resolvedFrom: "OZON-SKU-99"));
    }

    [Fact]
    public void Host_MayUpdateExternalProductId_NewListing_Allows()
    {
        Assert.True(MarketplaceExportIdentity.MayUpdateExternalProductId(
            previousLinkExternalId: "stale-or-missing",
            pinnedExternalIdPassedToAdapter: null,
            resultExternalId: "777",
            resolvedFrom: null));
    }

    [Fact]
    public void Yandex_ResolveOfferId_Linked_PrefersExistingExternalId()
    {
        var offerId = YandexOfferIdentity.ResolveOfferId(
            existingExternalId: "bound-offer",
            sku: "local-sku",
            offerIdFromPayload: "payload-offer");

        Assert.Equal("bound-offer", offerId);
    }

    [Fact]
    public void Yandex_ResolveOfferId_Unlinked_UsesSku()
    {
        var offerId = YandexOfferIdentity.ResolveOfferId(
            existingExternalId: null,
            sku: "local-sku",
            offerIdFromPayload: "payload-offer");

        Assert.Equal("local-sku", offerId);
    }
}
