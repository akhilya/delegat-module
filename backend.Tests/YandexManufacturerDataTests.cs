using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.YandexMarket;
using Delegat.Marketplace.YandexMarket.Models;

namespace Delegat.Tests;

/// <summary>
/// Covers vendorCode / manufacturer / manufacturerCountries wiring added in the Yandex adapter bump.
/// </summary>
public class YandexManufacturerDataTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void CapManufacturerCountries_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(YandexParameterHelpers.CapManufacturerCountries(null));
        Assert.Null(YandexParameterHelpers.CapManufacturerCountries(Array.Empty<string>()));
        Assert.Null(YandexParameterHelpers.CapManufacturerCountries(new[] { "  ", "" }));
    }

    [Fact]
    public void CapManufacturerCountries_LimitsToFive()
    {
        var input = new[] { "Россия", "Китай", "Турция", "Индия", "Вьетнам", "Беларусь", "Казахстан" };
        var capped = YandexParameterHelpers.CapManufacturerCountries(input);

        Assert.NotNull(capped);
        Assert.Equal(5, capped!.Count);
        Assert.Equal(new[] { "Россия", "Китай", "Турция", "Индия", "Вьетнам" }, capped);
    }

    [Fact]
    public void ApplyManufacturerFields_SetsManufacturerAndCountries()
    {
        var offer = new UpdateOfferDto { OfferId = "SKU-1" };

        YandexParameterHelpers.ApplyManufacturerFields(
            offer,
            "ООО Ромашка, Москва, ОГРН 123",
            new[] { "Россия", "Китай", "лишняя1", "лишняя2", "лишняя3", "лишняя4" });

        Assert.Equal("ООО Ромашка, Москва, ОГРН 123", offer.Manufacturer);
        Assert.Equal(5, offer.ManufacturerCountries!.Count);
        Assert.DoesNotContain("лишняя4", offer.ManufacturerCountries);
    }

    [Fact]
    public void ApplyManufacturerFields_WhitespaceManufacturer_LeavesNull()
    {
        var offer = new UpdateOfferDto { OfferId = "SKU-1" };

        YandexParameterHelpers.ApplyManufacturerFields(offer, "   ", new[] { "Россия" });

        Assert.Null(offer.Manufacturer);
        Assert.Equal(new List<string> { "Россия" }, offer.ManufacturerCountries);
    }

    [Fact]
    public void MergeManufacturerAttributes_JoinsCountriesAndSkipsBlankVendorCode()
    {
        var attrs = new Dictionary<string, object>();

        YandexParameterHelpers.MergeManufacturerAttributes(
            attrs,
            vendorCode: "  ",
            manufacturer: "Factory Ltd",
            manufacturerCountries: new List<string> { "Россия", "Китай" });

        Assert.False(attrs.ContainsKey("vendorCode"));
        Assert.Equal("Factory Ltd", attrs["manufacturer"]);
        Assert.Equal("Россия, Китай", attrs["manufacturerCountries"]);
    }

    [Fact]
    public void MergeManufacturerAttributes_SetsVendorCodeWhenPresent()
    {
        var attrs = new Dictionary<string, object>();

        YandexParameterHelpers.MergeManufacturerAttributes(
            attrs,
            vendorCode: "ART-42",
            manufacturer: null,
            manufacturerCountries: null);

        Assert.Equal("ART-42", attrs["vendorCode"]);
        Assert.False(attrs.ContainsKey("manufacturer"));
        Assert.False(attrs.ContainsKey("manufacturerCountries"));
    }

    [Fact]
    public void OfferDtos_SerializeManufacturerFieldsWithApiNames()
    {
        var update = new UpdateOfferDto
        {
            OfferId = "SKU-1",
            VendorCode = "VC-9",
            Manufacturer = "Maker",
            ManufacturerCountries = new List<string> { "Россия", "Китай" }
        };

        var json = JsonSerializer.Serialize(update, JsonOpts);
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            Assert.Equal("VC-9", root.GetProperty("vendorCode").GetString());
            Assert.Equal("Maker", root.GetProperty("manufacturer").GetString());
            var countries = root.GetProperty("manufacturerCountries").EnumerateArray()
                .Select(e => e.GetString())
                .ToList();
            Assert.Equal(new[] { "Россия", "Китай" }, countries);
        }

        var get = new GetOfferDto
        {
            OfferId = "SKU-1",
            VendorCode = "VC-9",
            Manufacturer = "Maker",
            ManufacturerCountries = new List<string> { "Россия" }
        };
        var getJson = JsonSerializer.Serialize(get, JsonOpts);
        using (var doc = JsonDocument.Parse(getJson))
        {
            Assert.Equal("VC-9", doc.RootElement.GetProperty("vendorCode").GetString());
            Assert.Equal(
                new[] { "Россия" },
                doc.RootElement.GetProperty("manufacturerCountries").EnumerateArray()
                    .Select(e => e.GetString())
                    .ToArray());
        }
    }

    [Fact]
    public async Task GetCategoryFields_ExposesManufacturerDataGroup()
    {
        var plugin = new YandexMarketPlugin();
        // Invalid/zero category id returns standard offer fields without calling Market API.
        var fields = await plugin.GetCategoryFieldsAsync(
            new ConnectionContext(),
            "0");

        var byKey = fields.ToDictionary(f => f.Key, StringComparer.Ordinal);

        Assert.True(byKey.ContainsKey("vendor"));
        Assert.True(byKey.ContainsKey("vendorCode"));
        Assert.True(byKey.ContainsKey("manufacturer"));
        Assert.True(byKey.ContainsKey("manufacturerCountries"));
        Assert.True(byKey.ContainsKey("barcodes"));

        Assert.Equal("Данные производителя", byKey["vendor"].Group);
        Assert.Equal("Данные производителя", byKey["vendorCode"].Group);
        Assert.Equal("Данные производителя", byKey["manufacturer"].Group);
        Assert.Equal("Данные производителя", byKey["manufacturerCountries"].Group);
        Assert.Equal("Данные производителя", byKey["barcodes"].Group);

        Assert.True(byKey["vendor"].IsRequired);
        Assert.False(byKey["vendorCode"].IsRequired);
        Assert.False(byKey["manufacturer"].IsRequired);
        Assert.False(byKey["manufacturerCountries"].IsRequired);
    }
}
