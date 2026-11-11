using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class AttributeImportNameNormalizerTests
{
    [Theory]
    [InlineData("height (mm)", "mm", "Высота")]
    [InlineData("Package width, mm", "mm", "Ширина упаковки")]
    [InlineData("gross_weight (kg)", "kg", "Вес брутто")]
    [InlineData("Высота (мм)", "mm", "Высота")]
    public void Normalize_TranslatesCommonNamesAndMovesUnitSuffixToUnitField(
        string sourceName,
        string unitCode,
        string expected)
        => Assert.Equal(expected, AttributeImportNameNormalizer.Normalize(sourceName, unitCode));

    [Fact]
    public void Normalize_CleansKnownUnitAndSnakeCaseButKeepsUnknownName()
        => Assert.Equal(
            "Custom specification",
            AttributeImportNameNormalizer.Normalize("Custom_specification (mm)", "mm"));

    [Theory]
    [InlineData("height with packaging (mm)", "mm", null, "Высота с упаковкой")]
    [InlineData("Высота", "mm", "Высота товара с упаковкой", "Высота с упаковкой")]
    [InlineData(
        "Height (mm)", "mm",
        "Product height in millimeters (including packaging)Product height in millimeters (including packaging)",
        "Высота с упаковкой")]
    [InlineData(
        "Height (mm)", "mm", "Product height in millimeters (without packaging)", "Высота без упаковки")]
    [InlineData("Height (mm)", "mm", "Height of the package in millimeters", "Высота упаковки")]
    [InlineData("Width (mm)", "mm", "Product width in millimeters including package", "Ширина с упаковкой")]
    [InlineData("Width (mm)", "mm", "Height of the package in millimeters", "Ширина")]
    [InlineData("Высота с упаковкой", "mm", null, "Высота с упаковкой")]
    public void Normalize_PreservesPackagingQualifier(
        string sourceName,
        string unitCode,
        string? description,
        string expected)
        => Assert.Equal(
            expected,
            AttributeImportNameNormalizer.Normalize(sourceName, unitCode, description));

    [Fact]
    public void Normalize_DoesNotInferPackagingQualifierFromUnrelatedDescription()
        => Assert.Equal(
            "Высота",
            AttributeImportNameNormalizer.Normalize(
                "Высота", "mm", "Height is required; keep the package secure."));

    [Fact]
    public void Normalize_DoesNotRemoveUnknownParenthesizedText()
        => Assert.Equal(
            "Custom field (v2)",
            AttributeImportNameNormalizer.Normalize("Custom field (v2)", null));
}
