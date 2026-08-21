using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using ProductMarketplaceStatus = Delegat.Enums.ProductMarketplaceStatus;

namespace Delegat.Tests.Infrastructure;

/// <summary>
/// Seeds common entities into an in-memory DataContext for tests.
/// </summary>
public sealed class TestDataBuilder
{
    private readonly DataContext _db;
    private int _userSeq = 1;
    private int _storeSeq = 1;
    private int _productSeq = 1;
    private int _categorySeq = 1;
    private int _groupSeq = 1;
    private int _attrSeq = 1;
    private int _connectionSeq = 1;
    private int _mappingSeq = 1;
    private int _linkSeq = 1;
    private int _operationSeq = 1;

    public TestDataBuilder(DataContext db) => _db = db;

    public User AddUser(
        string? userName = null,
        bool isAdmin = false,
        bool isActive = true,
        int? selectedStoreId = null)
    {
        var id = _userSeq++;
        var name = userName ?? $"user{id}";
        var user = new User
        {
            Id = id,
            UserName = name,
            Email = $"{name}@example.com",
            NormalizedUserName = name.ToUpperInvariant(),
            NormalizedEmail = $"{name}@example.com".ToUpperInvariant(),
            isAdmin = isAdmin,
            isActive = isActive,
            selectedStoreId = selectedStoreId,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        _db.Users.Add(user);
        return user;
    }

    public Store AddStore(
        string? name = null,
        string baseCurrency = "RUB",
        string timezone = "Europe/Moscow",
        params User[] members)
    {
        var id = _storeSeq++;
        var store = new Store
        {
            Id = id,
            name = name ?? $"Store {id}",
            baseCurrency = baseCurrency,
            timezone = timezone
        };
        foreach (var m in members)
            store.members.Add(m);
        _db.stores.Add(store);
        return store;
    }

    public void AddMember(Store store, User user, bool asAdmin = false)
    {
        store.members.Add(user);
        _ = asAdmin; // store-admin concept removed; kept for call-site compatibility
    }

    public Category AddCategory(int storeId, string? name = null, string? description = null, bool hasSizes = false)
    {
        var id = _categorySeq++;
        var cat = new Category
        {
            Id = id,
            storeId = storeId,
            name = name ?? $"Category {id}",
            description = description,
            hasSizes = hasSizes,
            createdAt = DateTime.UtcNow
        };
        _db.categories.Add(cat);
        return cat;
    }

    public CategoryAttribute AddCategoryAttribute(
        int categoryId,
        string? name = null,
        AttributeType type = AttributeType.Text,
        bool isRequired = false,
        int sortOrder = 0,
        bool isUnique = false,
        string? options = null,
        string? key = null)
    {
        var id = _attrSeq++;
        var attr = new CategoryAttribute
        {
            Id = id,
            categoryId = categoryId,
            name = name ?? $"Attr {id}",
            key = key ?? $"attr_{id}",
            type = type,
            isRequired = isRequired,
            isUnique = isUnique,
            sortOrder = sortOrder,
            options = options
        };
        _db.categoryAttributes.Add(attr);
        return attr;
    }

    public StoreAttribute AddStoreAttribute(
        int storeId,
        string? name = null,
        AttributeType type = AttributeType.Text,
        int sortOrder = 0,
        bool isUnique = false,
        string? options = null,
        string? key = null)
    {
        var id = _attrSeq++;
        var attr = new StoreAttribute
        {
            Id = id,
            storeId = storeId,
            name = name ?? $"StoreAttr {id}",
            key = key ?? $"store_attr_{id}",
            type = type,
            isUnique = isUnique,
            sortOrder = sortOrder,
            options = options
        };
        _db.storeAttributes.Add(attr);
        return attr;
    }

    public ProductVariant AddVariant(
        int productId,
        string? size = null,
        string? sku = null,
        string? barcode = null,
        decimal? price = null,
        int? quantity = null,
        int sortOrder = 0)
    {
        var variant = new ProductVariant
        {
            productId = productId,
            size = size ?? "M",
            sku = sku,
            barcode = barcode,
            price = price,
            quantity = quantity,
            sortOrder = sortOrder,
            createdAt = DateTime.UtcNow,
            updatedAt = DateTime.UtcNow
        };
        _db.productVariants.Add(variant);
        return variant;
    }

    public Product AddProduct(
        int storeId,
        string? name = null,
        string? sku = null,
        int? categoryId = null,
        decimal? price = 100m,
        DateTime? deletedAt = null,
        string? barcode = null)
    {
        var id = _productSeq++;
        var product = new Product
        {
            Id = id,
            storeId = storeId,
            name = name ?? $"Product {id}",
            sku = sku ?? $"SKU-{id}",
            barcode = barcode,
            description = "",
            categoryId = categoryId,
            price = price,
            createdAt = DateTime.UtcNow,
            updatedAt = DateTime.UtcNow,
            deletedAt = deletedAt
        };
        _db.products.Add(product);
        return product;
    }

    public ProductGroup AddGroup(int storeId, string? name = null, params Product[] products)
    {
        var id = _groupSeq++;
        var group = new ProductGroup
        {
            Id = id,
            storeId = storeId,
            name = name ?? $"Group {id}"
        };
        _db.productGroups.Add(group);
        var order = 0;
        foreach (var p in products)
        {
            _db.productGroupMembers.Add(new ProductGroupMember
            {
                groupId = id,
                productId = p.Id,
                sortOrder = order++,
                addedAt = DateTime.UtcNow
            });
        }
        return group;
    }

    public MarketplaceConnection AddConnection(
        int storeId,
        string adapterKey = "ozon",
        string? name = null,
        string? credentialsJson = "{\"apiKey\":\"secret\"}",
        bool isActive = true)
    {
        var id = _connectionSeq++;
        var conn = new MarketplaceConnection
        {
            Id = id,
            storeId = storeId,
            adapterKey = adapterKey,
            name = name ?? $"{adapterKey}-{id}",
            credentialsJson = credentialsJson,
            isActive = isActive,
            createdAt = DateTime.UtcNow
        };
        _db.marketplaceConnections.Add(conn);
        return conn;
    }

    public CategoryMapping AddMapping(
        int connectionId,
        int localCategoryId,
        string externalCategoryId = "ext-1",
        string? externalCategoryName = "External")
    {
        var id = _mappingSeq++;
        var mapping = new CategoryMapping
        {
            Id = id,
            connectionId = connectionId,
            localCategoryId = localCategoryId,
            externalCategoryId = externalCategoryId,
            externalCategoryName = externalCategoryName,
            name = $"Mapping {id}"
        };
        _db.categoryMappings.Add(mapping);
        return mapping;
    }

    public ProductMarketplaceLink AddProductLink(
        int connectionId,
        int productId,
        string externalProductId = "ext-prod-1",
        ProductMarketplaceStatus status = ProductMarketplaceStatus.Synced)
    {
        var id = _linkSeq++;
        var link = new ProductMarketplaceLink
        {
            Id = id,
            connectionId = connectionId,
            productId = productId,
            externalProductId = externalProductId,
            status = status,
            createdAt = DateTime.UtcNow
        };
        _db.productMarketplaceLinks.Add(link);
        return link;
    }

    public Operation AddOperation(
        int storeId,
        int authorUserId = 1,
        string type = "batch-export",
        string status = "running",
        string? operationId = null,
        string? title = null)
    {
        var opId = operationId ?? Guid.NewGuid().ToString("N");
        var op = new Operation
        {
            operationId = opId,
            storeId = storeId,
            authorUserId = authorUserId,
            type = type,
            title = title ?? type,
            status = status,
            createdAt = DateTime.UtcNow
        };
        _db.operations.Add(op);
        _operationSeq++;
        return op;
    }

    public async Task SaveAsync() => await _db.SaveChangesAsync();
}
