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
