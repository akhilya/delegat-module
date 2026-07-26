using System.Text.Json;
using Delegat.Enums;
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
}
