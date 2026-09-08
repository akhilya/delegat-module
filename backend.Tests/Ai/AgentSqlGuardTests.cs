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
}
