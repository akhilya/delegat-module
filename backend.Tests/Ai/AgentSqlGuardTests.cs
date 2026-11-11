using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class AgentSqlGuardTests
{
    [Fact]
    public void Accepts_SimpleSelectFromProducts()
    {
        var parsed = AgentSqlGuard.Parse("SELECT id, sku, name FROM products LIMIT 10");

        Assert.Null(parsed.Error);
        Assert.Equal("products", parsed.Table);
        Assert.Equal(new[] { "id", "sku", "name" }, parsed.Columns);
        Assert.Equal(10, parsed.Limit);
    }

    [Fact]
    public void Rejects_InsertAndOtherStoresTables()
    {
        Assert.NotNull(AgentSqlGuard.Parse("INSERT INTO products (name) VALUES ('x')").Error);
        Assert.NotNull(AgentSqlGuard.Parse("SELECT * FROM users").Error);
        Assert.NotNull(AgentSqlGuard.Parse("SELECT credentialsJson FROM connections").Error);
        Assert.NotNull(AgentSqlGuard.Parse("SELECT payloadJson FROM orders").Error);
        Assert.NotNull(AgentSqlGuard.Parse("SELECT id FROM products; DROP TABLE products").Error);
    }

    [Fact]
    public void Rejects_UnknownColumnOnAllowlistedTable()
    {
        Assert.NotNull(AgentSqlGuard.Parse("SELECT password FROM products").Error);
    }

    [Fact]
    public void Parses_WhereInAndDescriptionColumn()
    {
        var parsed = AgentSqlGuard.Parse(
            "SELECT id, name, description FROM products WHERE id IN (1, 16) LIMIT 5");

        Assert.Null(parsed.Error);
        Assert.Equal(AgentSqlOp.In, parsed.Where?[0].Op);
        Assert.Equal(new[] { "1", "16" }, parsed.Where?[0].Values);
        Assert.Contains("description", parsed.Columns);
    }

    [Fact]
    public void Accepts_CategoryAttributesTable_AndAliases()
    {
        var parsed = AgentSqlGuard.Parse(
            "SELECT id, name, key FROM category_attributes WHERE categoryId = 5");
        Assert.Null(parsed.Error);
        Assert.Equal("category_attributes", parsed.Table);

        var alias = AgentSqlGuard.Parse("SELECT id, value FROM product_attributes WHERE productId = 16");
        Assert.Null(alias.Error);
        Assert.Equal("product_attribute_values", alias.Table);
    }

    [Fact]
    public void Accepts_UnitCodeForCategoryAndStoreAttributes()
    {
        var category = AgentSqlGuard.Parse(
            "SELECT id, name, type, unitCode FROM category_attributes WHERE categoryId = 5");
        Assert.Null(category.Error);
        Assert.Contains("unitCode", category.Columns);

        var store = AgentSqlGuard.Parse("SELECT id, name, unitCode FROM store_attributes");
        Assert.Null(store.Error);
        Assert.Contains("unitCode", store.Columns);
    }

    [Fact]
    public void Rejects_ParentId_WithFlatCategoryHint()
    {
        var parsed = AgentSqlGuard.Parse("SELECT id, parent_id FROM categories");
        Assert.NotNull(parsed.Error);
        Assert.Contains("flat", parsed.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parses_WhereLikeAndOrder()
    {
        var parsed = AgentSqlGuard.Parse(
            "SELECT id, sku FROM products WHERE sku LIKE 'ABC%' ORDER BY name ASC LIMIT 5");

        Assert.Null(parsed.Error);
        Assert.Equal("sku", parsed.Where?[0].Column);
        Assert.Equal(AgentSqlOp.Like, parsed.Where?[0].Op);
        Assert.Equal("ABC%", parsed.Where?[0].Value);
        Assert.Equal("name", parsed.OrderByColumn);
        Assert.False(parsed.OrderDescending);
    }

    [Fact]
    public void Accepts_CountStarAndCountDistinct()
    {
        var parsed = AgentSqlGuard.Parse(
            "SELECT COUNT(*) AS product_count, COUNT(DISTINCT categoryId) AS categories_used FROM products");

        Assert.Null(parsed.Error);
        Assert.True(parsed.IsAggregate);
        Assert.Equal(new[] { "product_count", "categories_used" }, parsed.Columns);
        Assert.Equal(AgentSqlSelectKind.CountStar, parsed.Select[0].Kind);
        Assert.Equal(AgentSqlSelectKind.CountDistinct, parsed.Select[1].Kind);
        Assert.Equal("categoryId", parsed.Select[1].Column);
    }

    [Fact]
    public void Rejects_CountMixedWithRowColumns()
    {
        var parsed = AgentSqlGuard.Parse("SELECT id, COUNT(*) FROM products");
        Assert.NotNull(parsed.Error);
        Assert.Contains("COUNT", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_JoinWithOneTableHint()
    {
        var parsed = AgentSqlGuard.Parse(
            "SELECT pav.attributeId, ca.name FROM product_attribute_values pav JOIN category_attributes ca ON ca.id = pav.attributeId WHERE pav.productId = 1");
        Assert.NotNull(parsed.Error);
        Assert.Contains("JOIN", parsed.Error, StringComparison.Ordinal);
        Assert.Contains("one table", parsed.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_OrInWhere_WithAndHint()
    {
        var parsed = AgentSqlGuard.Parse(
            "SELECT sku, barcode FROM products WHERE barcode IS NOT NULL OR sku LIKE '%TEST%'");
        Assert.NotNull(parsed.Error);
        Assert.Contains("OR", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CategoryAttributeId_HintUsesAttributeId()
    {
        var parsed = AgentSqlGuard.Parse(
            "SELECT id, productId, categoryAttributeId, value FROM product_attribute_values WHERE productId = 1");
        Assert.NotNull(parsed.Error);
        Assert.Contains("attributeId", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_Gte_NotInSelect_Min_Distinct_AndOffset()
    {
        var gte = AgentSqlGuard.Parse("SELECT id, name FROM products WHERE id >= 1230 LIMIT 10");
        Assert.Null(gte.Error);
        Assert.Equal(AgentSqlOp.Gte, gte.Where?[0].Op);

        var notIn = AgentSqlGuard.Parse(
            "SELECT id, name, sku FROM products WHERE id NOT IN (SELECT productId FROM product_images)");
        Assert.Null(notIn.Error);
        Assert.Equal(AgentSqlOp.NotInSelect, notIn.Where?[0].Op);
        Assert.Equal("product_images", notIn.Where?[0].SubTable);

        var inPhotos = AgentSqlGuard.Parse(
            "SELECT id FROM products WHERE id IN (SELECT productId FROM product_images)");
        Assert.Null(inPhotos.Error);
        Assert.Equal(AgentSqlOp.InSelect, inPhotos.Where?[0].Op);

        var byValue = AgentSqlGuard.Parse(
            "SELECT id FROM products WHERE id IN (SELECT productId FROM store_product_attribute_values WHERE value = 'Cotton')");
        Assert.Null(byValue.Error);
        Assert.Equal(AgentSqlOp.InSelect, byValue.Where?[0].Op);
        Assert.Equal("Cotton", byValue.Where?[0].SubWhere?[0].Value);

        var min = AgentSqlGuard.Parse("SELECT MIN(productId) AS n FROM files");
        Assert.Null(min.Error);
        Assert.Equal(AgentSqlSelectKind.Min, min.Select[0].Kind);

        var distinct = AgentSqlGuard.Parse("SELECT DISTINCT productId FROM files LIMIT 20 OFFSET 50");
        Assert.Null(distinct.Error);
        Assert.True(distinct.IsDistinct);
        Assert.Equal(20, distinct.Limit);
        Assert.Equal(50, distinct.Offset);

        var contentType = AgentSqlGuard.Parse("SELECT DISTINCT contentType FROM files");
        Assert.Null(contentType.Error);
        Assert.True(contentType.IsDistinct);
        Assert.Equal("contentType", contentType.Select[0].Column);

        var notInFiles = AgentSqlGuard.Parse(
            "SELECT id, name, sku FROM products WHERE id NOT IN (SELECT productId FROM files)");
        Assert.Null(notInFiles.Error);
        Assert.Equal(AgentSqlOp.NotInSelect, notInFiles.Where?[0].Op);
        Assert.Equal("files", notInFiles.Where?[0].SubTable);

        var isNull = AgentSqlGuard.Parse("SELECT id, sku FROM products WHERE barcode IS NULL");
        Assert.Null(isNull.Error);
        Assert.Equal(AgentSqlOp.IsNull, isNull.Where?[0].Op);
    }

    [Fact]
    public void Parses_ShowTables_AndDescribeColumns()
    {
        var tables = AgentSqlGuard.Parse("SHOW TABLES");
        Assert.Null(tables.Error);
        Assert.True(tables.IsSchema);
        Assert.Equal(new[] { "table_name" }, tables.Columns);
        Assert.Null(tables.SchemaFilterTable);

        var describe = AgentSqlGuard.Parse("DESCRIBE products");
        Assert.Null(describe.Error);
        Assert.True(describe.IsSchema);
        Assert.Equal("products", describe.SchemaFilterTable);
        Assert.Equal(new[] { "column_name" }, describe.Columns);
        Assert.Contains("storageQuantity", AgentSqlGuard.ColumnsOf("products")!);

        var fromSchema = AgentSqlGuard.Parse(
            "SELECT table_name, column_name FROM schema WHERE table_name = 'category_attributes'");
        Assert.Null(fromSchema.Error);
        Assert.Equal("category_attributes", fromSchema.SchemaFilterTable);
        Assert.Equal(new[] { "table_name", "column_name" }, fromSchema.Columns);
    }

    [Fact]
    public void WhereTemplates_ExpandToFixedSql_AndAttacksNeverParse()
    {
        var combined = CatalogWhereTemplates.Expand("@withGallery AND price >= 1000 AND @noCategory");
        var parsed = AgentSqlGuard.TryParseWhere("products", combined);
        Assert.Null(parsed.Error);
        Assert.Equal(AgentSqlOp.InSelect, parsed.Predicates![0].Op);
        Assert.Equal("product_images", parsed.Predicates[0].SubTable);
        Assert.Equal(AgentSqlOp.Gte, parsed.Predicates[1].Op);
        Assert.Equal(AgentSqlOp.IsNull, parsed.Predicates[2].Op);

        var quoted = CatalogWhereTemplates.Expand("name = '@withGallery'");
        Assert.Contains("'@withGallery'", quoted, StringComparison.Ordinal);
        Assert.Null(AgentSqlGuard.TryParseWhere("products", quoted).Error);

        foreach (var name in CatalogWhereTemplates.All)
        {
            var one = AgentSqlGuard.TryParseWhere("products", CatalogWhereTemplates.Expand("@" + name.Name));
            Assert.Null(one.Error);
        }

        string[] attacks =
        [
            "id = 1; DROP TABLE products",
            "@withGallery; DELETE FROM products",
            "id = 1 OR 1=1",
            "name = 'x' OR '1'='1'",
            "@withGallery OR @inStock",
            "id = 1 -- comment",
            "id = 1 /* comment */",
            "id IN (SELECT password FROM users)",
            "id IN (SELECT productId FROM product_images) UNION SELECT id FROM products",
            "id IN (SELECT productId FROM information_schema.tables)",
            "id IN (SELECT productId FROM products)",
            "name = 'a\\' OR id IS NOT NULL",
            "@nope",
            "price >= 1; SELECT SLEEP(5)",
        ];
        foreach (var attack in attacks)
        {
            string? expanded = null;
            try
            {
                expanded = CatalogWhereTemplates.Expand(attack);
            }
            catch (AgentSqlFilterException)
            {
                continue;
            }

            Assert.NotNull(AgentSqlGuard.TryParseWhere("products", expanded!).Error);
        }
    }

    [Fact]
    public void Rejects_SchemaOnUnknownTable_AndInformationSchema()
    {
        Assert.NotNull(AgentSqlGuard.Parse("SHOW COLUMNS FROM users").Error);
        Assert.NotNull(AgentSqlGuard.Parse("SELECT table_name FROM information_schema.tables").Error);
        Assert.NotNull(AgentSqlGuard.Parse("SELECT * FROM schema").Error);
    }
}
