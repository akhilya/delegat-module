using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;

namespace Delegat.Tests;

public class FieldAutoMappingTests
{
    private static CategoryAttribute Attribute(int id, string name, AttributeType type, string? unit = null) =>
        new() { Id = id, name = name, key = $"attribute_{id}", type = type, unitCode = unit };

    [Theory]
    [InlineData("model", "Model", "select")]
    [InlineData("descriptive_code", "Characteristic", "string")]
    public void DoesNotBindUnrelatedFieldsToProductFields(string key, string name, string type)
    {
        var result = FieldAutoMapping.Resolve(new ExternalField(key, name, true, type), []);
        Assert.Equal(FieldMappingSourceType.Static, result.sourceType);
    }

    [Fact]
    public void MatchesRenamedAttributeAndRejectsAmbiguousCandidates()
    {
        var field = new ExternalField("manufacturer_color", "Product color", false, "string");
        var first = Attribute(1, "Product color", AttributeType.Text);
        var second = Attribute(2, "Product color", AttributeType.Text);
        Assert.Equal(1, FieldAutoMapping.Resolve(field, [first]).sourceAttributeId);
        Assert.Equal(FieldMappingSourceType.Static, FieldAutoMapping.Resolve(field, [first, second]).sourceType);
    }

    [Fact]
    public void ConvertsCompatibleUnitsButRejectsIncompatibleTypesAndUnits()
    {
        var field = new ExternalField("weight", "Weight", false, "number", UnitCode: "g");
        var kg = Attribute(1, "Weight", AttributeType.Number, "kg");
        var result = FieldAutoMapping.Resolve(field, [kg]);
        Assert.Equal(1, result.sourceAttributeId);
        var conversion = FieldAutoMapping.Conversion(field, kg);
        Assert.Contains("unitConversion", conversion);
        Assert.Contains("kg", conversion);
        Assert.Contains("g", conversion);
        Assert.Equal(FieldMappingSourceType.Static, FieldAutoMapping.Resolve(field,
            [Attribute(2, "Weight", AttributeType.Number, "m"), Attribute(3, "Weight", AttributeType.Text)]).sourceType);
    }
    [Fact]
    public void SyncAddsConversionChainWithoutChangingExistingBindings()
    {
        var existing = GraphGenerator.BuildGraphJson([new FieldSpec
        {
            externalFieldKey = "existing", sourceType = FieldMappingSourceType.ProductField, sourceField = "sku"
        }]);
        var field = new ExternalField("weight", "Weight", false, "number", UnitCode: "g");
        var kg = Attribute(1, "Weight", AttributeType.Number, "kg");
        var updated = GraphGenerator.SyncAdapterFields(existing, [new FieldSpec
        {
            externalFieldKey = "weight", externalFieldName = "Weight", externalFieldType = "number",
            sourceType = FieldMappingSourceType.CategoryAttribute, sourceAttributeId = kg.Id,
            modifiersJson = FieldAutoMapping.Conversion(field, kg)
        }]);
        Assert.Contains("unitConversion", updated);
        Assert.Contains("existing", updated);
        Assert.Contains("sku", updated);
        Assert.Contains("f:weight", updated);
    }

}
