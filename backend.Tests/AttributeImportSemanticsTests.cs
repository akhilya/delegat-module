using Delegat.Enums;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class AttributeImportSemanticsTests
{
    [Theory]
    [InlineData("Озон.Видеообложка: ссылка", "string", "Видеообложка")]
    [InlineData("Озон.Видео: ссылка", "string", "Видео")]
    [InlineData("Ozon.Video: URL", "string", "Video")]
    [InlineData("Ссылка на сертификат", "string", "Ссылка на сертификат")]
    [InlineData("Инструкция (файл)", "string", "Инструкция")]
    [InlineData("Документ: URL", "url", "Документ")]
    [InlineData("Video", "url", "Video")]
    [InlineData("Custom attachment", "string", "Custom attachment")]
    [InlineData("Specification", " FILE ", "Specification")]
    [InlineData("Manual", "attachment", "Manual")]
    public void AttachmentFields_UseFileTypeAndReadableNames(string name, string wireType, string expectedName)
    {
        var field = new ExternalField("custom", name, false, wireType);
        Assert.Equal(AttributeType.File, AttributeImportSemantics.ResolveType(field));
        Assert.Equal(expectedName, AttributeImportSemantics.NormalizeName(field, null));
        Assert.Equal(wireType, field.Type);
        Assert.Equal(name, field.Name);
    }

    [Theory]
    [InlineData("Озон.Видео: название", "string", AttributeType.Text)]
    [InlineData("Название файла", "string", AttributeType.Text)]
    [InlineData("Video title", "string", AttributeType.Text)]
    [InlineData("Видео: длительность", "number", AttributeType.Number)]
    [InlineData("Видео", "boolean", AttributeType.Boolean)]
    [InlineData("Video format", "select", AttributeType.Select)]
    [InlineData("Ссылка на сайт производителя", "string", AttributeType.Text)]
    [InlineData("Product page", "url", AttributeType.Text)]
    public void UnrelatedFields_KeepTheirTypes(string name, string wireType, AttributeType expected)
        => Assert.Equal(expected, AttributeImportSemantics.ResolveType(
            new ExternalField("custom", name, false, wireType)));

    [Fact]
    public void DictionaryAndDescription_DoNotTriggerAttachmentInference()
    {
        Assert.Equal(AttributeType.Text, AttributeImportSemantics.ResolveType(
            new ExternalField("custom", "Видео: ссылка", false, "string",
                [new ExternalFieldOption("1", "Yes")])));
        Assert.Equal(AttributeType.Text, AttributeImportSemantics.ResolveType(
            new ExternalField("custom", "Details", false, "string", Description: "Ссылка на файл")));
    }
}
