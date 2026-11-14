using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class CategoryImportServiceTests
{
    [Fact]
    public async Task Import_LeavesAmbiguousLegacyMatchesUnwired()
    {
        var (svc, db, connection) = Create("leaf", [new ExternalField("colour", "Product color", true, "string")]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var first = data.AddCategoryAttribute(category.Id, "Product color", key: "one");
        var second = data.AddCategoryAttribute(category.Id, "Product color", key: "two");
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", localCategoryId: category.Id,
            requireDuplicateDecisions: false);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var graph = (await db.categoryMappings.SingleAsync()).graphJson!;
        Assert.DoesNotContain($"\"attributeId\":{first.Id}", graph);
        Assert.DoesNotContain($"\"attributeId\":{second.Id}", graph);
        Assert.Equal(2, await db.categoryAttributes.CountAsync());
    }

    [Fact]
    public async Task Import_ReusesRenamedSimilarAttributeForLegacyJob()
    {
        var (svc, db, connection) = Create("leaf", [new ExternalField("ext_width", "Ширина товара", true, "string")]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var existing = data.AddCategoryAttribute(category.Id, "Ширина товара общая", key: "local_width");
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", localCategoryId: category.Id,
            requireDuplicateDecisions: false);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(0, result.AttributesCreated);
        Assert.Contains($"\"attributeId\":{existing.Id}", (await db.categoryMappings.SingleAsync()).graphJson);
    }

    [Fact]
    public async Task Import_ReusesConvertibleUnitWithConversionInGraph()
    {
        var (svc, db, connection) = Create("leaf", [new ExternalField("mass", "Weight", true, "number", UnitCode: "g")]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var existing = data.AddCategoryAttribute(category.Id, "Вес", AttributeType.Number, key: "local_weight");
        existing.unitCode = "kg";
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", localCategoryId: category.Id,
            attributeDecisions: [new CategoryImportAttributeDecision { fieldKey = "mass", action = "reuse", existingAttributeId = existing.Id }]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var graph = (await db.categoryMappings.SingleAsync()).graphJson!;
        Assert.Contains("unitConversion", graph);
        Assert.Contains("\"fromUnit\":\"kg\"", graph);
        Assert.Contains("\"toUnit\":\"g\"", graph);
        Assert.Contains($"\"attributeId\":{existing.Id}", graph);
    }

    [Fact]
    public async Task Import_DoesNotTreatDictionaryCharacteristicsAsProductFields()
    {
        var (svc, db, connection) = Create("leaf", [
            new ExternalField("model", "Model", true, "select"),
            new ExternalField("sku_color", "SKU color", true, "select"),
            new ExternalField("descriptive_code", "Characteristic", true, "string")]);
        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Products");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(3, await db.categoryAttributes.CountAsync());
        var graph = (await db.categoryMappings.SingleAsync()).graphJson!;
        Assert.DoesNotContain("\"field\":\"sku\"", graph);
        Assert.DoesNotContain("\"field\":\"description\"", graph);
    }

    [Fact]
    public async Task Import_ReusesNormalizedFileDuplicateWithoutMutatingDefinition()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("21841", "Озон.Видео: ссылка", false, "string", MaxValues: 3),
        ]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var existing = data.AddCategoryAttribute(category.Id, "Видео", AttributeType.File, key: "video");
        var text = data.AddCategoryAttribute(category.Id, "Видео", AttributeType.Text, key: "video_text");
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(
            connection, "leaf", localCategoryId: category.Id,
            attributeDecisions:
            [
                new CategoryImportAttributeDecision
                {
                    fieldKey = "21841", action = "reuse", existingAttributeId = existing.Id,
                },
            ]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(0, result.AttributesCreated);
        Assert.Equal(2, await db.categoryAttributes.CountAsync());
        Assert.False(existing.allowMultipleFiles);
        Assert.Equal("video", existing.key);
        Assert.Equal(AttributeType.Text, text.type);
        var mapping = await db.categoryMappings.SingleAsync();
        Assert.Contains($"\"attributeId\":{existing.Id}", mapping.graphJson);
    }

    [Fact]
    public async Task Import_CreatesFileAttributesWithoutRemoteStringConstraints()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("21841", "Озон.Видео: ссылка", false, "string",
                MaxLength: 100, ValidationPattern: "^https://", MaxValues: 3,
                AllowCustomValues: true),
            new ExternalField("cover", "Озон.Видеообложка: ссылка", false, "string", MaxValues: 1),
            new ExternalField("manual", "Manual", false, "file"),
            new ExternalField("21837", "Озон.Видео: название", false, "string"),
        ]);

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Toys");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var video = await db.categoryAttributes.SingleAsync(a => a.key == "21841");
        Assert.Equal("Видео", video.name);
        Assert.Equal(AttributeType.File, video.type);
        Assert.True(video.allowMultipleFiles);
        Assert.Null(video.maxLength);
        Assert.Null(video.validationPattern);
        Assert.Null(video.maxValues);
        Assert.Null(video.defaultValue);
        Assert.Null(video.options);
        Assert.False(video.allowCustomValues);
        var cover = await db.categoryAttributes.SingleAsync(a => a.key == "cover");
        Assert.Equal("Видеообложка", cover.name);
        Assert.Equal(AttributeType.File, cover.type);
        Assert.False(cover.allowMultipleFiles);
        Assert.Equal(AttributeType.File, (await db.categoryAttributes.SingleAsync(a => a.key == "manual")).type);
        Assert.Equal(AttributeType.Text, (await db.categoryAttributes.SingleAsync(a => a.key == "21837")).type);

        var mapping = await db.categoryMappings.SingleAsync();
        using var graph = System.Text.Json.JsonDocument.Parse(mapping.graphJson!);
        var fields = graph.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("type").GetString() == "adapterFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray();
        var remoteVideo = fields.Single(f => f.GetProperty("fieldKey").GetString() == "21841");
        Assert.Equal("Озон.Видео: ссылка", remoteVideo.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Import_ClipsOverlongCategoryAndAttributeNames()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("brand", new string('B', 180), false, "string"),
        ]);

        var result = await svc.ImportExternalCategoryAsync(
            connection,
            "leaf",
            localCategoryName: new string('C', 180),
            externalCategoryName: new string('E', 600));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var category = await db.categories.SingleAsync();
        Assert.Equal(Category.CATEGORY_NAME_MAX_LENGTH, category.name.Length);

        var mapping = await db.categoryMappings.SingleAsync();
        Assert.Equal(CategoryMapping.EXTERNAL_CATEGORY_NAME_MAX_LENGTH, mapping.externalCategoryName!.Length);

        var brand = await db.categoryAttributes.SingleAsync(a => a.key == "brand");
        Assert.Equal(CategoryAttribute.ATTRIBUTE_NAME_MAX_LENGTH, brand.name.Length);
    }

    [Fact]
    public void AttributeGroupIconValue_ForAdapter_LowercasesKey()
    {
        Assert.Equal("adapter:wildberries", AttributeGroupIconValue.ForAdapter("Wildberries"));
        Assert.Equal("", AttributeGroupIconValue.ForAdapter("  "));
    }

    [Fact]
    public async Task Import_SetsMarketplaceGroupAndAdapterIcon()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("brand", "Brand", false, "string"),
        ]);

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Toys");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var brand = await db.categoryAttributes.SingleAsync(a => a.key == "brand");
        Assert.Equal("Fake", brand.group);
        Assert.Equal("adapter:fake", brand.groupIcon);
    }

    [Fact]
    public async Task Import_CopiesMarketplaceFieldConstraints()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField(
                "material",
                "Material",
                false,
                "string",
                Description: "Fiber and composition details",
                MaxLength: 80,
                ValidationPattern: "^[A-Za-z]+$"),
            new ExternalField(
                "weight_g",
                "Weight",
                false,
                "number",
                MinValue: 1,
                MaxValue: 5000),
            new ExternalField(
                "unit_hack",
                "Unit field",
                false,
                "string",
                ValidationPattern: "unitId=42"),
            new ExternalField(
                "color",
                "Color",
                false,
                "select",
                AllowCustomValues: true),
            new ExternalField(
                "tags",
                "Tags",
                false,
                "multiselect",
                MaxValues: 3),
        ]);

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Toys");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var material = await db.categoryAttributes.SingleAsync(a => a.key == "material");
        Assert.Equal("Fiber and composition details", material.description);
        Assert.True(material.descriptionInTooltip);
        Assert.Equal(80, material.maxLength);
        Assert.Equal("^[A-Za-z]+$", material.validationPattern);

        var weight = await db.categoryAttributes.SingleAsync(a => a.key == "weight_g");
        Assert.Equal(1m, weight.minValue);
        Assert.Equal(5000m, weight.maxValue);

        var unitHack = await db.categoryAttributes.SingleAsync(a => a.key == "unit_hack");
        Assert.Null(unitHack.validationPattern);

        var color = await db.categoryAttributes.SingleAsync(a => a.key == "color");
        Assert.True(color.allowCustomValues);
        Assert.False(color.isRequired);
        Assert.False(material.isRequired);
        Assert.False(weight.isRequired);

        var tags = await db.categoryAttributes.SingleAsync(a => a.key == "tags");
        Assert.Equal(3, tags.maxValues);
    }

    [Fact]
    public async Task Import_InferUnitsFromNumericFieldNamesAndDescriptions()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("height", "height (mm)", false, "number"),
            new ExternalField("width", "Package width (mm)", false, "number"),
            new ExternalField("weight", "Gross weight", false, "number",
                Description: "Enter the product weight in kilograms."),
            new ExternalField("color_code", "Color C", false, "string"),
        ]);

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Toys");
        Assert.True(result.IsSuccess, result.ErrorMessage);

        var height = await db.categoryAttributes.SingleAsync(a => a.key == "height");
        var width = await db.categoryAttributes.SingleAsync(a => a.key == "width");
        var weight = await db.categoryAttributes.SingleAsync(a => a.key == "weight");
        var color = await db.categoryAttributes.SingleAsync(a => a.key == "color_code");
        Assert.Equal("Высота", height.name);
        Assert.Equal("mm", height.unitCode);
        Assert.Equal("mm", width.unitCode);
        Assert.Equal("kg", weight.unitCode);
        Assert.Null(color.unitCode);

        var mapping = await db.categoryMappings.SingleAsync();
        using var graph = System.Text.Json.JsonDocument.Parse(mapping.graphJson!);
        var importedFields = graph.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("type").GetString() == "adapterFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("fieldKey").GetString()!);
        Assert.Equal("mm", importedFields["width"].GetProperty("unitCode").GetString());
        Assert.Equal("мм", importedFields["width"].GetProperty("unit").GetString());
        Assert.Equal("kg", importedFields["weight"].GetProperty("unitCode").GetString());
        Assert.Equal("height (mm)", importedFields["height"].GetProperty("label").GetString());
    }

    [Fact]
    public async Task Import_KeepsHeightWithPackagingSeparateWhenOzonDescriptionDisambiguatesName()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField(
                "height", "Height (mm)", false, "number",
                Description: "Product height in millimeters"),
            new ExternalField(
                "height_with_packaging", "Height (mm)", false, "number",
                Description: "Product height in millimeters (including packaging)"
                    + "Product height in millimeters (including packaging)"),
        ]);

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Toys");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.AttributesCreated);
        Assert.Equal("Высота", (await db.categoryAttributes.SingleAsync(a => a.key == "height")).name);
        var packagedHeight = await db.categoryAttributes
            .SingleAsync(a => a.key == "height_with_packaging");
        Assert.Equal("Высота с упаковкой", packagedHeight.name);
        Assert.Equal("mm", packagedHeight.unitCode);
    }

    [Fact]
    public async Task Import_RequiresAnExplicitChoiceForLikelyDuplicateAndDoesNotMutateExistingAttribute()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField(
                "height_with_packaging", "Height (mm)", false, "number",
                Description: "Product height in millimeters (including packaging)"),
        ]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var existing = data.AddCategoryAttribute(
            category.Id,
            name: "Высота с упаковкой",
            type: AttributeType.Number,
            key: "height_with_packaging");
        existing.unitCode = "mm";
        existing.description = "Local description";
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(
            connection, "leaf", localCategoryId: category.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("Choose whether to reuse, create, or skip", result.ErrorMessage);
        Assert.Equal("Высота с упаковкой", existing.name);
        Assert.Equal("mm", existing.unitCode);
        Assert.Equal("Local description", existing.description);
        Assert.Single(await db.categoryAttributes.ToListAsync());
    }

    [Fact]
    public async Task Import_ValidatesAllDuplicateChoicesBeforeTrackingAnyNewAttributes()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("material", "Material", false, "string"),
            new ExternalField(
                "height_with_packaging", "Height (mm)", false, "number",
                Description: "Product height in millimeters (including packaging)"),
        ]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var existing = data.AddCategoryAttribute(
            category.Id,
            name: "Высота с упаковкой",
            type: AttributeType.Number,
            key: "height_with_packaging");
        existing.unitCode = "mm";
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(
            connection,
            "leaf",
            localCategoryId: category.Id);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(
            db.ChangeTracker.Entries<CategoryAttribute>(),
            entry => entry.State == EntityState.Added);
        Assert.DoesNotContain(
            db.ChangeTracker.Entries<CategoryMapping>(),
            entry => entry.State == EntityState.Added);
        Assert.Single(await db.categoryAttributes.ToListAsync());
    }

    [Fact]
    public async Task Import_ExplicitReuseUsesChosenDuplicateWithoutChangingItsDefinition()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField(
                "height_with_packaging", "Height (mm)", false, "number",
                Description: "Product height in millimeters (including packaging)"),
        ]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var existing = data.AddCategoryAttribute(
            category.Id,
            name: "Высота с упаковкой",
            type: AttributeType.Number,
            key: "custom_height");
        existing.unitCode = "mm";
        existing.description = "Keep this text";
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(
            connection,
            "leaf",
            localCategoryId: category.Id,
            attributeDecisions:
            [
                new CategoryImportAttributeDecision
                {
                    fieldKey = "height_with_packaging",
                    action = "reuse",
                    existingAttributeId = existing.Id,
                },
            ]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(0, result.AttributesCreated);
        Assert.Equal("Высота с упаковкой", existing.name);
        Assert.Equal("custom_height", existing.key);
        Assert.Equal("mm", existing.unitCode);
        Assert.Equal("Keep this text", existing.description);
        Assert.Single(await db.categoryAttributes.ToListAsync());

        var graph = await db.categoryMappings.SingleAsync();
        Assert.Contains($"\"attributeId\":{existing.Id}", graph.graphJson);
    }

    [Fact]
    public async Task Import_ExplicitCreateLeavesPossibleDuplicateUntouched()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField(
                "height_with_packaging", "Height (mm)", false, "number",
                Description: "Product height in millimeters (including packaging)"),
        ]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var existing = data.AddCategoryAttribute(
            category.Id,
            name: "Высота с упаковкой",
            type: AttributeType.Number,
            key: "custom_height");
        existing.unitCode = "mm";
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(
            connection,
            "leaf",
            localCategoryId: category.Id,
            attributeDecisions:
            [
                new CategoryImportAttributeDecision
                {
                    fieldKey = "height_with_packaging",
                    action = "create",
                },
            ]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, result.AttributesCreated);
        Assert.Equal("custom_height", existing.key);
        Assert.Equal("mm", existing.unitCode);
        Assert.Equal(2, await db.categoryAttributes.CountAsync());
    }

    [Fact]
    public async Task Import_ExplicitSkipDoesNotCreateAnAttributeOrBindTheField()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField(
                "height_with_packaging", "Height (mm)", false, "number",
                Description: "Product height in millimeters (including packaging)"),
        ]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var existing = data.AddCategoryAttribute(
            category.Id,
            name: "Высота с упаковкой",
            type: AttributeType.Number,
            key: "height_with_packaging");
        existing.unitCode = "mm";
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(
            connection,
            "leaf",
            localCategoryId: category.Id,
            attributeDecisions:
            [
                new CategoryImportAttributeDecision
                {
                    fieldKey = "height_with_packaging",
                    action = "skip",
                },
            ]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(0, result.AttributesCreated);
        Assert.Single(await db.categoryAttributes.ToListAsync());
        var mapping = await db.categoryMappings.SingleAsync();
        Assert.DoesNotContain($"\"attributeId\":{existing.Id}", mapping.graphJson);
    }

    [Fact]
    public async Task Import_RejectsReuseDecisionForNewCategoryBeforeCreatingIt()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("height", "Height", false, "string"),
        ]);

        var result = await svc.ImportExternalCategoryAsync(
            connection,
            "leaf",
            attributeDecisions:
            [
                new CategoryImportAttributeDecision
                {
                    fieldKey = "height",
                    action = "reuse",
                    existingAttributeId = 42,
                },
            ]);

        Assert.False(result.IsSuccess);
        Assert.Empty(await db.categories.ToListAsync());
        Assert.Empty(await db.categoryAttributes.ToListAsync());
        Assert.Empty(await db.categoryMappings.ToListAsync());
    }

    [Fact]
    public async Task Import_EmptyFieldSelectionDoesNotBindUnselectedExistingAttributes()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("height", "Height", false, "string"),
        ]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var existing = data.AddCategoryAttribute(
            category.Id,
            name: "Height",
            type: AttributeType.Text,
            key: "height");
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(
            connection,
            "leaf",
            localCategoryId: category.Id,
            selectedFieldKeys: []);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(0, result.AttributesCreated);
        var mapping = await db.categoryMappings.SingleAsync();
        Assert.DoesNotContain($"\"attributeId\":{existing.Id}", mapping.graphJson);
    }

    [Fact]
    public async Task Import_DoesNotMarkLocalAttributesRequired()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("brand", "Brand", true, "string"),
            new ExternalField("weight", "Weight", true, "number"),
        ]);

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Toys");
        Assert.True(result.IsSuccess, result.ErrorMessage);

        var brand = await db.categoryAttributes.SingleAsync(a => a.key == "brand");
        var weight = await db.categoryAttributes.SingleAsync(a => a.key == "weight");
        Assert.False(brand.isRequired);
        Assert.False(weight.isRequired);
    }

    [Fact]
    public void BarcodeFields_AreBaseProductFieldsMappedToProductBarcode()
    {
        Assert.True(CategoryImportService.IsBaseOrSpecialField(
            new ExternalField("barcode", "Barcode (EAN)", false, "string")));
        Assert.True(CategoryImportService.IsBaseOrSpecialField(
            new ExternalField("barcodes", "Barcodes", false, "string")));
        Assert.True(CategoryImportService.IsBaseOrSpecialField(
            new ExternalField("odd-key", "Serial", false, "barcode")));
        Assert.False(CategoryImportService.IsBaseOrSpecialField(
            new ExternalField("brand", "Brand", false, "string")));

        var (sourceType, sourceField, sourceAttributeId) = CategoryImportService.DetermineAutoMapping(
            new ExternalField("barcode", "Barcode", false, "string"),
            new Dictionary<string, int> { ["barcode"] = 99 });

        Assert.Equal(FieldMappingSourceType.ProductField, sourceType);
        Assert.Equal("barcode", sourceField);
        Assert.Null(sourceAttributeId);
    }

    [Fact]
    public void PackagingWeightCharacteristic_IsNotAutoMapped()
    {
        var map = new Dictionary<string, int> { ["charc:88952"] = 2, ["charc:999"] = 3 };
        var (sourceType, sourceField, sourceAttributeId) = CategoryImportService.DetermineAutoMapping(
            new ExternalField("charc:88952", "Вес товара с упаковкой (г)", false, "number"),
            map);
        Assert.Equal(FieldMappingSourceType.Static, sourceType);
        Assert.Null(sourceField);
        Assert.Null(sourceAttributeId);

        var (netType, _, netAttr) = CategoryImportService.DetermineAutoMapping(
            new ExternalField("charc:999", "Вес товара без упаковки", false, "number"),
            map);
        Assert.Equal(FieldMappingSourceType.CategoryAttribute, netType);
        Assert.Equal(3, netAttr);
    }

    [Fact]
    public void ProductNameDuplicates_AreBaseNameFields()
    {
        Assert.True(CategoryImportService.IsNameField("product_name", "Product name"));
        Assert.True(CategoryImportService.IsNameField("productname", "Product name"));
        Assert.True(CategoryImportService.IsBaseOrSpecialField(
            new ExternalField("product_name", "Product name", false, "string")));
        Assert.False(CategoryImportService.IsNameField("10097", "Color name"));
    }

    [Fact]
    public async Task Import_AliasesDuplicateNameFieldInsteadOfSecondProductBinding()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("name", "Name", true, "string", SortOrder: 0),
            new ExternalField("product_name", "Product name", false, "string", SortOrder: 20),
            new ExternalField("brand", "Brand", false, "string", SortOrder: 30),
        ]);

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Toys");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(await db.categoryAttributes.AnyAsync(a => a.key == "product_name"));
        Assert.True(await db.categoryAttributes.AnyAsync(a => a.key == "brand"));

        var mapping = await db.categoryMappings.SingleAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(mapping.graphJson!);
        var fields = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("type").GetString() == "adapterFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("fieldKey").GetString()!);

        Assert.Equal("name", fields["name"].GetProperty("source").GetProperty("field").GetString());
        Assert.Equal("name", fields["product_name"].GetProperty("aliasOf").GetString());
    }

    [Fact]
    public async Task Import_AliasesFieldsBoundToSameCategoryAttribute()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("shade", "Shade", true, "string", SortOrder: 0),
            new ExternalField("31565", "Shade", false, "string", SortOrder: 10),
        ]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var attr = data.AddCategoryAttribute(category.Id, "Shade", AttributeType.Text, key: "shade");
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", localCategoryId: category.Id,
            attributeDecisions:
            [
                new CategoryImportAttributeDecision { fieldKey = "shade", action = "reuse", existingAttributeId = attr.Id },
                new CategoryImportAttributeDecision { fieldKey = "31565", action = "reuse", existingAttributeId = attr.Id },
            ]);

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var mapping = await db.categoryMappings.SingleAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(mapping.graphJson!);
        var fields = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("type").GetString() == "adapterFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("fieldKey").GetString()!);

        // The named field keeps the binding; the numeric attribute id aliases it.
        Assert.Equal(attr.Id, fields["shade"].GetProperty("source").GetProperty("attributeId").GetInt32());
        Assert.Equal("shade", fields["31565"].GetProperty("aliasOf").GetString());
        Assert.False(fields["31565"].TryGetProperty("source", out _));
    }

    [Fact]
    public async Task Import_MarksAttributeFieldRepeatingNamedProductField()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("name", "Name", true, "string", SortOrder: 0),
            new ExternalField("31565", "Название", false, "string", SortOrder: 10),
        ]);
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "Existing");
        var attr = data.AddCategoryAttribute(category.Id, "Название", AttributeType.Text, key: "name");
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", localCategoryId: category.Id,
            attributeDecisions:
            [
                new CategoryImportAttributeDecision { fieldKey = "31565", action = "reuse", existingAttributeId = attr.Id },
            ]);

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var mapping = await db.categoryMappings.SingleAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(mapping.graphJson!);
        var fields = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("type").GetString() == "adapterFields")
            .GetProperty("data").GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("fieldKey").GetString()!);

        // Visual-only dedup: 31565 keeps its own attribute binding but is flagged as a repeat.
        Assert.Equal("name", fields["name"].GetProperty("source").GetProperty("field").GetString());
        Assert.Equal("name", fields["31565"].GetProperty("repeatOf").GetString());
        Assert.Equal(attr.Id, fields["31565"].GetProperty("source").GetProperty("attributeId").GetInt32());
    }

    [Fact]
    public async Task Import_SkipsBarcodeAttribute()
    {
        var (svc, db, connection) = Create("leaf",
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("barcode", "Barcode", false, "string"),
            new ExternalField("brand", "Brand", false, "string"),
        ]);

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Toys");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, result.AttributesCreated);
        Assert.False(await db.categoryAttributes.AnyAsync(a => a.key == "barcode"));
        Assert.True(await db.categoryAttributes.AnyAsync(a => a.key == "brand"));
    }

    [Fact]
    public async Task Import_CreatesManyAttributesInOneGo()
    {
        var extra = Enumerable.Range(1, 20)
            .Select(i => new ExternalField($"attr_{i}", $"Attribute {i}", false, "string"))
            .ToList();
        extra.Insert(0, new ExternalField("sku", "SKU", true, "string"));

        var (svc, db, connection) = Create("leaf", extra);

        var result = await svc.ImportExternalCategoryAsync(connection, "leaf", "Toys");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(20, result.AttributesCreated);
        Assert.Equal(20, await db.categoryAttributes.CountAsync());
    }

    [Fact]
    public async Task Import_IntoExistingCategory_KeepsSeparateMappingsPerExternalType()
    {
        var (svc, db, connection) = Create(
            "type-a",
            [
                new ExternalField("sku", "SKU", true, "string"),
                new ExternalField("brand", "Brand", false, "string"),
            ],
            extraFields: new Dictionary<string, List<ExternalField>>(StringComparer.Ordinal)
            {
                ["type-b"] =
                [
                    new ExternalField("sku", "SKU", true, "string"),
                    new ExternalField("color", "Color", false, "string"),
                ],
            });

        var category = new TestDataBuilder(db).AddCategory(connection.storeId, "Local");
        await db.SaveChangesAsync();

        var first = await svc.ImportExternalCategoryAsync(
            connection, "type-a", localCategoryId: category.Id, externalCategoryName: "Type A");
        var second = await svc.ImportExternalCategoryAsync(
            connection, "type-b", localCategoryId: category.Id, externalCategoryName: "Type B");

        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.NotEqual(first.CategoryMappingId, second.CategoryMappingId);

        var mappings = await db.categoryMappings.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, mappings.Count);
        Assert.Equal("type-a", mappings[0].externalCategoryId);
        Assert.Equal("type-b", mappings[1].externalCategoryId);
        Assert.All(mappings, m => Assert.Equal(category.Id, m.localCategoryId));
    }

    [Fact]
    public async Task Import_IntoExisting_UsesMarketplaceFullPathWhenCallerOmitsName()
    {
        const string extId = "17028973:115944384";
        var (svc, db, connection) = Create(
            extId,
            [
                new ExternalField("sku", "SKU", true, "string"),
                new ExternalField("brand", "Brand", false, "string"),
            ],
            leafCategory: new ExternalCategory(
                extId,
                "T-shirt",
                FullPath: "Apparel / T-shirt",
                HasChildren: false));

        var category = new TestDataBuilder(db).AddCategory(connection.storeId, "Shirts");
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(
            connection, extId, localCategoryId: category.Id);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var mapping = await db.categoryMappings.SingleAsync();
        Assert.Equal("Apparel / T-shirt", mapping.externalCategoryName);
        Assert.Equal("Apparel / T-shirt", mapping.name);
        Assert.DoesNotContain("Category ", mapping.externalCategoryName);
    }

    [Fact]
    public async Task Import_IntoExisting_FallsBackToLocalNameWhenMarketplaceNameUnknown()
    {
        const string extId = "17028973:115944384";
        var (svc, db, connection) = Create(
            extId,
            [
                new ExternalField("sku", "SKU", true, "string"),
                new ExternalField("brand", "Brand", false, "string"),
            ]);

        var category = new TestDataBuilder(db).AddCategory(connection.storeId, "Shirts");
        await db.SaveChangesAsync();

        var result = await svc.ImportExternalCategoryAsync(
            connection, extId, localCategoryId: category.Id);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var mapping = await db.categoryMappings.SingleAsync();
        Assert.Equal("Shirts", mapping.externalCategoryName);
        Assert.Equal("Shirts", mapping.name);
    }

    private static (
        CategoryImportService svc,
        DataContext db,
        MarketplaceConnection connection) Create(
        string leafId,
        List<ExternalField> fields,
        Dictionary<string, List<ExternalField>>? extraFields = null,
        ExternalCategory? leafCategory = null)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        db.SaveChanges();

        var fieldsByCategoryId = new Dictionary<string, List<ExternalField>>(StringComparer.Ordinal)
        {
            [leafId] = fields,
        };
        if (extraFields != null)
        {
            foreach (var (id, extra) in extraFields)
                fieldsByCategoryId[id] = extra;
        }

        var root = fieldsByCategoryId.Keys
            .Select(id =>
                leafCategory != null && id == leafId
                    ? leafCategory
                    : new ExternalCategory(id, id, HasChildren: false))
            .ToList();

        var adapter = FakeCategoryAdapter.Create(
            root,
            children: new Dictionary<string, List<ExternalCategory>>(StringComparer.Ordinal),
            fieldsByCategoryId);

        var svc = new CategoryImportService(db, new SingleAdapterRegistry(adapter));
        return (svc, db, connection);
    }

    private sealed class SingleAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
        public IMarketplaceAdapter? GetByKey(string key) =>
            key == adapter.Key ? adapter : null;
        public bool Exists(string key) => key == adapter.Key;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }
}
