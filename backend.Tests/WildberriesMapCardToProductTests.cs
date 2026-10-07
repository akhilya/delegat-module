using System.Text.Json;
using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.Wildberries.Models;

namespace Delegat.Tests;

public class WildberriesMapCardToProductTests
{
    [Fact]
    public void MapCardToProduct_PutsDimensionsAndBrandOnAttributes()
    {
        var card = new WbCard
        {
            NmId = 11,
            VendorCode = "ART-1",
            Title = "Tee",
            Brand = "Acme",
            SubjectId = 42,
            Dimensions = new WbDimensions
            {
                Length = 20,
                Width = 10,
                Height = 5,
                WeightBrutto = 0.15,
            },
        };

        var product = WildberriesPlugin.MapCardToProduct(card);

        Assert.Equal("Acme", product.Brand);
        Assert.NotNull(product.Attributes);
        Assert.Equal(20, product.Attributes!["length"]);
        Assert.Equal(10, product.Attributes["width"]);
        Assert.Equal(5, product.Attributes["height"]);
        Assert.Equal(0.15, Assert.IsType<double>(product.Attributes["weight"]));
        Assert.Equal("Acme", product.Attributes["brand"]);
        Assert.Null(product.Price);
        Assert.Equal("42", product.CategoryId);
    }

    [Fact]
    public void MapCardToProduct_OmitsCategoryWhenSubjectIdIsZero()
    {
        var card = new WbCard
        {
            NmId = 11,
            VendorCode = "ART-1",
            Title = "Tee",
            SubjectId = 0,
        };

        var product = WildberriesPlugin.MapCardToProduct(card);

        Assert.Null(product.CategoryId);
        Assert.Null(product.CategoryName);
    }

    [Fact]
    public void PackagingWeightCharacteristic_IsNotSent_AndCardWeightStaysKilograms()
    {
        Assert.True(WildberriesPlugin.IsPackagingWeightCharacteristic("charc:88952", null));
        Assert.True(WildberriesPlugin.IsPackagingWeightCharacteristic("charc:1", "Вес товара с упаковкой (г)"));
        Assert.False(WildberriesPlugin.IsPackagingWeightCharacteristic("charc:2", "Вес товара без упаковки"));

        var data = new Dictionary<string, object>
        {
            ["weight"] = 0.06,
            ["charc:88952"] = 60,
            ["charc:17596"] = "пластик",
            ["_fieldLabels"] = new Dictionary<string, string>
            {
                ["charc:88952"] = "Вес товара с упаковкой (г)",
                ["charc:17596"] = "Материал изделия",
            },
        };

        Assert.Equal(0.06, WildberriesPlugin.ResolveWeightBruttoKilograms(data));
        var ids = WildberriesPlugin.BuildCharacteristics(data)!.Select(c => c.Id).ToList();
        Assert.DoesNotContain(88952, ids);
        Assert.Contains(17596, ids);
    }

    [Fact]
    public void PackagingWeightCharacteristic_FillsWeightBruttoWhenCardWeightIsMissing()
    {
        var data = new Dictionary<string, object>
        {
            ["charc:88952"] = 60,
        };
        Assert.Equal(0.06, WildberriesPlugin.ResolveWeightBruttoKilograms(data));
    }

    [Fact]
    public void SelectCharacteristics_StayStringLists_NotNumbers()
    {
        var data = new Dictionary<string, object>
        {
            ["charc:15000001"] = "9503003500",
            ["charc:15001405"] = "22",
            ["charc:90745"] = 12,
            ["_fieldTypes"] = new Dictionary<string, string>
            {
                ["charc:15000001"] = "select",
                ["charc:15001405"] = "select",
                ["charc:90745"] = "number",
            },
        };

        var chars = WildberriesPlugin.BuildCharacteristics(data)!;
        Assert.Equal(new[] { "9503003500" }, Assert.IsAssignableFrom<IEnumerable<string>>(chars.Single(c => c.Id == 15000001).Value));
        Assert.Equal(new[] { "22" }, Assert.IsAssignableFrom<IEnumerable<string>>(chars.Single(c => c.Id == 15001405).Value));
        Assert.Equal(12, chars.Single(c => c.Id == 90745).Value);

        var requests = WildberriesPlugin.CardErrorListRequests(new DateTime(2026, 9, 28, 20, 0, 0, DateTimeKind.Utc));
        Assert.False(requests[0].Order!.Ascending);
        Assert.Equal("2026-09-28T20:00:00Z", requests[0].Cursor!.UpdatedAt);
        Assert.True(requests[1].Order!.Ascending);
    }

    [Fact]
    public void CardCreateWait_KeepsGoingUntilCatalogOrDraftError()
    {
        var max = WildberriesPlugin.CardCreateMaxWait;
        Assert.Equal(TimeSpan.FromMinutes(30), max);
        Assert.Equal(
            WildberriesPlugin.CardCreateConfirmation.Created,
            WildberriesPlugin.ClassifyCardCreateWait(true, true, TimeSpan.FromSeconds(20), max));
        Assert.Equal(
            WildberriesPlugin.CardCreateConfirmation.DraftError,
            WildberriesPlugin.ClassifyCardCreateWait(false, true, TimeSpan.FromMinutes(5), max));
        Assert.Equal(
            WildberriesPlugin.CardCreateConfirmation.KeepWaiting,
            WildberriesPlugin.ClassifyCardCreateWait(false, false, TimeSpan.FromMinutes(29), max));
        Assert.Equal(
            WildberriesPlugin.CardCreateConfirmation.TimedOut,
            WildberriesPlugin.ClassifyCardCreateWait(false, false, max, max));

        var unresolved = WildberriesPlugin.CardCreateUnresolvedMessage(max);
        Assert.Contains("was not sent again", unresolved, StringComparison.Ordinal);
        Assert.DoesNotContain("may still be processing", unresolved, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TimeSpan.FromMinutes(8), WildberriesPlugin.CardCreateBulkMaxWait);
        Assert.True(WildberriesPlugin.IsCardUpdatedBefore("2026-10-07T11:00:00Z", new DateTime(2026, 10, 7, 11, 40, 0, DateTimeKind.Utc)));
        Assert.False(WildberriesPlugin.IsCardUpdatedBefore("2026-10-07T11:50:00Z", new DateTime(2026, 10, 7, 11, 40, 0, DateTimeKind.Utc)));
        Assert.False(WildberriesPlugin.IsCardUpdatedBefore(null, new DateTime(2026, 10, 7, 11, 40, 0, DateTimeKind.Utc)));

        var errors = new Dictionary<string, List<string>>
        {
            ["_FI43"] = ["Недопустимое значение ТНВЭД"],
        };
        Assert.Equal("Недопустимое значение ТНВЭД", WildberriesPlugin.VendorErrorText(errors, "_fi43"));
    }

    [Fact]
    public void BarcodeCharacteristic_IsSentAsGtin_AndStaysInSkusOnce()
    {
        Assert.True(WildberriesPlugin.IsGtinCharacteristic("charc:14177453", null));
        Assert.True(WildberriesPlugin.IsGtinCharacteristic("charc:1", "Штрихкод"));
        Assert.True(WildberriesPlugin.IsGtinCharacteristic("charc:2", "GTIN"));
        Assert.False(WildberriesPlugin.IsGtinCharacteristic("charc:17596", "Материал изделия"));

        var data = new Dictionary<string, object>
        {
            ["name"] = "Mini",
            ["barcode"] = "2037374506867",
            ["charc:14177453"] = "2037374506867",
            ["charc:17596"] = "смола",
            ["price"] = 3900,
        };

        var chars = WildberriesPlugin.BuildCharacteristics(data)!;
        Assert.DoesNotContain(14177453, chars.Select(c => c.Id));
        Assert.Contains(17596, chars.Select(c => c.Id));

        var variant = WildberriesPlugin.BuildVariant(data, "_WA151");
        Assert.Equal("2037374506867", variant.Gtin);
        Assert.Equal(new[] { "2037374506867" }, variant.Sizes![0].Skus);
    }

    [Fact]
    public void GtinCharacteristic_IsAddedBesideTheExistingBarcode()
    {
        var data = new Dictionary<string, object>
        {
            ["barcode"] = "2037374506867",
            ["charc:999"] = "04601234567890",
            ["_fieldLabels"] = new Dictionary<string, string>
            {
                ["charc:999"] = "GTIN",
            },
        };

        Assert.Null(WildberriesPlugin.BuildCharacteristics(data));
        var variant = WildberriesPlugin.BuildVariant(data, "SKU");
        Assert.Equal("04601234567890", variant.Gtin);
        Assert.Equal(new[] { "2037374506867", "04601234567890" }, variant.Sizes![0].Skus);
    }

    [Fact]
    public void CardUpdate_MovesExistingBarcodeCharacteristicOntoGtin()
    {
        var card = new WbCard
        {
            NmId = 5,
            VendorCode = "WA151",
            Characteristics = new List<WbCardCharacteristic>
            {
                new()
                {
                    Id = 14177453,
                    Name = "Штрихкод",
                    Value = JsonDocument.Parse("\"2037374506867\"").RootElement.Clone(),
                },
                new()
                {
                    Id = 17596,
                    Name = "Материал",
                    Value = JsonDocument.Parse("\"смола\"").RootElement.Clone(),
                },
            },
            Sizes = new List<WbCardSize>
            {
                new() { ChrtId = 9, Skus = new List<string> { "2037374506867" } },
            },
        };

        var item = WildberriesPlugin.BuildUpdateItem(card, new Dictionary<string, object>());
        var ids = item.Characteristics!.Select(c => c.Id).ToList();
        Assert.Equal("2037374506867", item.Gtin);
        Assert.DoesNotContain(14177453, ids);
        Assert.Contains(17596, ids);
        Assert.Equal(new[] { "2037374506867" }, item.Sizes![0].Skus);
    }
}
