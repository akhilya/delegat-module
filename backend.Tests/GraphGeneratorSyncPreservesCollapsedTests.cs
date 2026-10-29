using System.Text.Json;
using Delegat.Enums;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;

namespace Delegat.Tests;

public class GraphGeneratorSyncPreservesCollapsedTests
{
    private const string GraphWithCollapsedSource = """
        {
          "nodes": [
            {
              "id": "src-1",
              "type": "productField",
              "position": { "x": 10, "y": 20 },
              "data": { "field": "name", "label": "Name", "collapsed": true }
            },
            {
              "id": "adapter-fields",
              "type": "adapterFields",
              "deletable": false,
              "position": { "x": 400, "y": 0 },
              "data": {
                "fields": [
                  { "fieldKey": "title", "label": "Title", "outputType": "string", "isRequired": true }
                ]
              }
            },
            {
              "id": "adapter-price-fields",
              "type": "adapterPriceFields",
              "deletable": false,
              "position": { "x": 400, "y": 200 },
              "data": {
                "fields": [
                  { "fieldKey": "price", "label": "Price", "outputType": "number", "isRequired": true }
                ]
              }
            }
          ],
          "edges": []
        }
        """;

    [Fact]
    public void SyncPriceFields_PreservesCollapsedOnSourceNodes()
    {
        var result = GraphGenerator.SyncPriceFields(
            GraphWithCollapsedSource,
            [
                new FieldSpec
                {
                    externalFieldKey = "oldPrice",
                    externalFieldName = "Old price",
                    externalFieldType = "number",
                    isRequired = false,
                    isEnabled = true,
                    sortOrder = 1,
                    sourceType = FieldMappingSourceType.ProductField,
                },
            ]);

        using var doc = JsonDocument.Parse(result);
        var src = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("id").GetString() == "src-1");

        Assert.True(src.GetProperty("data").GetProperty("collapsed").GetBoolean());
        Assert.Equal("Name", src.GetProperty("data").GetProperty("label").GetString());
    }

    [Fact]
    public void SyncPriceFields_MigratesLegacyPriceKeysFromAdapterFields()
    {
        const string graph = """
            {
              "nodes": [
                {
                  "id": "adapter-fields",
                  "type": "adapterFields",
                  "data": {
                    "fields": [
                      { "fieldKey": "sku", "label": "SKU", "outputType": "string", "isRequired": true },
                      {
                        "fieldKey": "sale_price",
                        "label": "Sale price",
                        "outputType": "number",
                        "isRequired": false,
                        "source": { "type": "productField", "field": "discountedPrice" }
                      }
                    ]
                  }
                }
              ],
              "edges": []
            }
            """;

        var result = GraphGenerator.SyncPriceFields(
            graph,
            [
                new FieldSpec
                {
                    externalFieldKey = "price",
                    externalFieldName = "Price",
                    externalFieldType = "number",
                    isRequired = true,
                    isEnabled = true,
                    sortOrder = 0,
                    sourceType = FieldMappingSourceType.ProductField,
                    sourceField = "price",
                },
                new FieldSpec
                {
                    externalFieldKey = "sale_price",
                    externalFieldName = "Sale price",
                    externalFieldType = "number",
                    isRequired = false,
                    isEnabled = true,
                    sortOrder = 1,
                    sourceType = FieldMappingSourceType.ProductField,
                    sourceField = "discountedPrice",
                },
            ]);

        using var doc = JsonDocument.Parse(result);
        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();

        var adapterFields = nodes.First(n => n.GetProperty("type").GetString() == "adapterFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("fieldKey").GetString())
            .ToList();
        Assert.Contains("sku", adapterFields);
        Assert.DoesNotContain("sale_price", adapterFields);

        var priceFields = nodes.First(n => n.GetProperty("type").GetString() == "adapterPriceFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray().ToList();
        Assert.Contains(priceFields, f => f.GetProperty("fieldKey").GetString() == "price");
        var sale = priceFields.First(f => f.GetProperty("fieldKey").GetString() == "sale_price");
        Assert.Equal("productField", sale.GetProperty("source").GetProperty("type").GetString());
        Assert.Equal("discountedPrice", sale.GetProperty("source").GetProperty("field").GetString());
    }

    [Fact]
    public void SyncAdapterFields_PreservesCollapsedOnSourceNodes()
    {
        var result = GraphGenerator.SyncAdapterFields(
            GraphWithCollapsedSource,
            [
                new FieldSpec
                {
                    externalFieldKey = "description",
                    externalFieldName = "Description",
                    externalFieldType = "string",
                    isRequired = false,
                    isEnabled = true,
                    sortOrder = 2,
                    sourceType = FieldMappingSourceType.ProductField,
                },
            ]);

        using var doc = JsonDocument.Parse(result);
        var src = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("id").GetString() == "src-1");

        Assert.True(src.GetProperty("data").GetProperty("collapsed").GetBoolean());
    }

    [Fact]
    public void BuildGraphJson_AliasesSecondaryNameFieldToPrimary()
    {
        var json = GraphGenerator.BuildGraphJson(
        [
            new FieldSpec
            {
                externalFieldKey = "product_name",
                externalFieldName = "Product name",
                externalFieldType = "string",
                isEnabled = true,
                sortOrder = 20,
                sourceType = FieldMappingSourceType.ProductField,
                sourceField = "name",
            },
            new FieldSpec
            {
                externalFieldKey = "name",
                externalFieldName = "Name",
                externalFieldType = "string",
                isEnabled = true,
                sortOrder = 0,
                sourceType = FieldMappingSourceType.ProductField,
                sourceField = "name",
            },
            new FieldSpec
            {
                externalFieldKey = "brand",
                externalFieldName = "Brand",
                externalFieldType = "string",
                isEnabled = true,
                sortOrder = 30,
                sourceType = FieldMappingSourceType.CategoryAttribute,
                sourceAttributeId = 7,
            },
        ]);

        using var doc = JsonDocument.Parse(json);
        var fields = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("type").GetString() == "adapterFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("fieldKey").GetString()!);

        var name = fields["name"];
        Assert.Equal("productField", name.GetProperty("source").GetProperty("type").GetString());
        Assert.Equal("name", name.GetProperty("source").GetProperty("field").GetString());
        Assert.False(name.TryGetProperty("aliasOf", out var nameAlias) && nameAlias.ValueKind != JsonValueKind.Null);

        var duplicate = fields["product_name"];
        Assert.Equal("name", duplicate.GetProperty("aliasOf").GetString());
        Assert.False(duplicate.TryGetProperty("source", out var dupSource) && dupSource.ValueKind != JsonValueKind.Null);

        var brand = fields["brand"];
        Assert.Equal("attribute", brand.GetProperty("source").GetProperty("type").GetString());
    }

    [Fact]
    public void BuildPriceFieldSpecs_OmitsDiscountedPriceWhenExtendedOff()
    {
        var fields = new List<ExternalField>
        {
            new("price", "Price", true, "number", Description: "Current price", Group: "Prices", SortOrder: 0),
            new("discountedPrice", "Discounted price", false, "number"),
            new("sale_price", "Sale price", false, "number"),
            new("oldPrice", "Old price", false, "number"),
        };

        var off = CategoryImportService.BuildPriceFieldSpecs(fields, new Dictionary<string, int>());
        Assert.DoesNotContain(off, s => s.externalFieldKey is "discountedPrice" or "sale_price");
        Assert.Contains(off, s => s.externalFieldKey == "oldPrice");
        Assert.Contains(off, s => s.externalFieldKey == "price" && s.sourceField == "price");
        var price = Assert.Single(off, s => s.externalFieldKey == "price");
        Assert.Equal("Prices", price.externalFieldGroup);
        Assert.Equal("Current price", price.externalFieldDescription);

        var on = CategoryImportService.BuildPriceFieldSpecs(
            fields, new Dictionary<string, int>(), extendedPriceParameters: true);
        var discounted = Assert.Single(on, s => s.externalFieldKey == "discountedPrice");
        Assert.Equal("discountedPrice", discounted.sourceField);
        var sale = Assert.Single(on, s => s.externalFieldKey == "sale_price");
        Assert.Equal("discountedPrice", sale.sourceField);
    }

    [Fact]
    public void SyncPriceFields_RefreshesSchemaMetadataAndPreservesBindings()
    {
        const string graph = """
            {
              "nodes": [
                {
                  "id": "adapter-price-fields",
                  "type": "adapterPriceFields",
                  "data": {
                    "fields": [
                      {
                        "fieldKey": "price",
                        "label": "Stale price label",
                        "outputType": "string",
                        "isRequired": false,
                        "group": "Old group",
                        "source": { "type": "productField", "field": "price" }
                      },
                      {
                        "fieldKey": "minPrice",
                        "label": "Stale minimum",
                        "outputType": "string",
                        "isRequired": false,
                        "omit": true
                      }
                    ]
                  }
                }
              ],
              "edges": [
                { "id": "edge-price", "source": "src-price", "target": "adapter-price-fields", "targetHandle": "f:price" }
              ]
            }
            """;

        var result = GraphGenerator.SyncPriceFields(graph, [
            new FieldSpec
            {
                externalFieldKey = "minPrice",
                externalFieldName = "Minimum price",
                externalFieldType = "number",
                externalFieldGroup = "Price strategy",
                externalFieldDescription = "Promotion floor",
                isRequired = true,
                isEnabled = true,
                sortOrder = 0,
                sourceType = FieldMappingSourceType.Static,
            },
            new FieldSpec
            {
                externalFieldKey = "price",
                externalFieldName = "Price",
                externalFieldType = "number",
                externalFieldGroup = "Prices",
                externalFieldDescription = "Current seller price",
                isRequired = true,
                isEnabled = true,
                sortOrder = 1,
                sourceType = FieldMappingSourceType.Static,
            },
        ]);

        using var doc = JsonDocument.Parse(result);
        var fields = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("type").GetString() == "adapterPriceFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray().ToList();

        Assert.Equal("minPrice", fields[0].GetProperty("fieldKey").GetString());
        Assert.Equal("Minimum price", fields[0].GetProperty("label").GetString());
        Assert.Equal("number", fields[0].GetProperty("outputType").GetString());
        Assert.Equal("Price strategy", fields[0].GetProperty("group").GetString());
        Assert.Equal("Promotion floor", fields[0].GetProperty("description").GetString());
        Assert.True(fields[0].GetProperty("isRequired").GetBoolean());
        Assert.True(fields[0].GetProperty("omit").GetBoolean());

        Assert.Equal("Price", fields[1].GetProperty("label").GetString());
        Assert.Equal("Prices", fields[1].GetProperty("group").GetString());
        Assert.Equal("productField", fields[1].GetProperty("source").GetProperty("type").GetString());
        Assert.Equal("price", fields[1].GetProperty("source").GetProperty("field").GetString());
        Assert.Equal("edge-price", Assert.Single(doc.RootElement.GetProperty("edges").EnumerateArray())
            .GetProperty("id").GetString());
    }

    [Fact]
    public void SyncPriceFields_DropsDiscountedPriceWhenSpecsOmitIt()
    {
        const string graph = """
            {
              "nodes": [
                {
                  "id": "adapter-price-fields",
                  "type": "adapterPriceFields",
                  "data": {
                    "fields": [
                      { "fieldKey": "price", "label": "Price", "outputType": "number", "isRequired": true },
                      {
                        "fieldKey": "discountedPrice",
                        "label": "Discounted price",
                        "outputType": "number",
                        "source": { "type": "productField", "field": "discountedPrice" }
                      },
                      { "fieldKey": "oldPrice", "label": "Old price", "outputType": "number" }
                    ]
                  }
                }
              ],
              "edges": [
                {
                  "id": "e-sale",
                  "source": "src-sale",
                  "target": "adapter-price-fields",
                  "targetHandle": "f:discountedPrice"
                }
              ]
            }
            """;

        var result = GraphGenerator.SyncPriceFields(graph, [
            new FieldSpec
            {
                externalFieldKey = "price",
                externalFieldName = "Price",
                externalFieldType = "number",
                isRequired = true,
                isEnabled = true,
                sortOrder = 0,
                sourceType = FieldMappingSourceType.ProductField,
                sourceField = "price",
            },
            new FieldSpec
            {
                externalFieldKey = "oldPrice",
                externalFieldName = "Old price",
                externalFieldType = "number",
                isRequired = false,
                isEnabled = true,
                sortOrder = 1,
                sourceType = FieldMappingSourceType.Static,
            },
        ]);

        using var doc = JsonDocument.Parse(result);
        var priceFields = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("type").GetString() == "adapterPriceFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("fieldKey").GetString())
            .ToList();
        Assert.Contains("price", priceFields);
        Assert.Contains("oldPrice", priceFields);
        Assert.DoesNotContain("discountedPrice", priceFields);
        var edges = doc.RootElement.GetProperty("edges").EnumerateArray().ToList();
        Assert.DoesNotContain(edges, e => e.GetProperty("targetHandle").GetString() == "f:discountedPrice");
    }
}
