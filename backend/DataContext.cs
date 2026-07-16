using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Ai;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;

namespace Delegat;

public class DataContext : IdentityDbContext<User, UserRole, int>
{
    public DbSet<Store> stores { get; set; }
    public DbSet<StoreCurrency> storeCurrencies { get; set; }
    public DbSet<Product> products { get; set; }
    public DbSet<Models.File> files { get; set; }
    public DbSet<StoreFile> storeFiles { get; set; }
    public DbSet<StoreFolder> storeFolders { get; set; }
    public DbSet<StoreFileTag> storeFileTags { get; set; }
    public DbSet<ImageReference> images { get; set; }
    public DbSet<PendingImageDownload> pendingImageDownloads { get; set; }
    public DbSet<PendingProductImport> pendingProductImports { get; set; }
    public DbSet<PendingProductExport> pendingProductExports { get; set; }
    public DbSet<PendingProductTransfer> pendingProductTransfers { get; set; }
    public DbSet<InvitationToken> invitationTokens { get; set; }
    public DbSet<Note> notes { get; set; }
    public DbSet<FileTag> tags { get; set; }
    public DbSet<TagDefinition> tagDefinitions { get; set; }
    public DbSet<ProductFolder> productFolders { get; set; }

    public DbSet<Category> categories { get; set; }
    public DbSet<CategoryAttribute> categoryAttributes { get; set; }
    public DbSet<ProductAttributeValue> productAttributeValues { get; set; }

    public DbSet<StoreAttribute> storeAttributes { get; set; }
    public DbSet<StoreProductAttributeValue> storeProductAttributeValues { get; set; }

    // Marketplace integration
    public DbSet<MarketplaceConnection> marketplaceConnections { get; set; }
    public DbSet<CategoryMapping> categoryMappings { get; set; }
    public DbSet<MappingGenerativeCache> mappingGenerativeCaches { get; set; }
    public DbSet<ProductMarketplaceLink> productMarketplaceLinks { get; set; }

    // Auth
    public DbSet<RefreshToken> refreshTokens { get; set; }

    // File sharing
    public DbSet<FileShareToken> fileShareTokens { get; set; }
    public DbSet<FolderShareToken> folderShareTokens { get; set; }
    public DbSet<StoreFileShareToken> storeFileShareTokens { get; set; }
    public DbSet<StoreFolderShareToken> storeFolderShareTokens { get; set; }

    // System versioning
    public DbSet<SystemVersion> systemVersions { get; set; }

    // Product variants (sizes)
    public DbSet<ProductVariant> productVariants { get; set; }
    public DbSet<ProductVariantLink> productVariantLinks { get; set; }

    // Product grouping
    public DbSet<ProductGroup> productGroups { get; set; }
    public DbSet<ProductGroupMember> productGroupMembers { get; set; }
    public DbSet<ProductGroupMarketplaceLink> productGroupMarketplaceLinks { get; set; }
    public DbSet<GroupValueMarketplaceLink> groupValueMarketplaceLinks { get; set; }

    // Operations history
    public DbSet<Operation> operations { get; set; }
    public DbSet<OperationLogEntry> operationLogEntries { get; set; }

    // Persisted server errors for administrators
    public DbSet<ErrorLog> errorLogs { get; set; }

    // Outbound marketplace HTTP request logs
    public DbSet<AdapterHttpLog> adapterHttpLogs { get; set; }

    // Platform settings editable by administrators
    public DbSet<AppSetting> appSettings { get; set; }

    public DbSet<AiModelConfig> aiModelConfigs { get; set; }
    public DbSet<AiOperation> aiOperations { get; set; }
    public DbSet<AiStoreSettings> aiStoreSettings { get; set; }
    public DbSet<AiConversation> aiConversations { get; set; }
    public DbSet<AiConversationMessage> aiConversationMessages { get; set; }

    public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Ignore<IdentityUserLogin<int>>();
        builder.Ignore<IdentityUserToken<int>>();
        builder.Entity<IdentityUserRole<int>>().HasKey(r => r.UserId);

        builder.Entity<User>()
            .HasOne(u => u.selectedStore)
            .WithMany()
            .HasForeignKey(u => u.selectedStoreId);

        builder.Entity<Store>()
            .HasMany(s => s.storeAdmins)
            .WithMany(u => u.storeAdminMemberships)
            .UsingEntity<Dictionary<string, object>>(
                "StoreAdministrators",
                right => right.HasOne<User>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne<Store>().WithMany().HasForeignKey("StoreId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.HasKey("StoreId", "UserId");
                    join.HasIndex("UserId");
                });

        // SystemVersion unique key
        builder.Entity<SystemVersion>()
            .HasIndex(sv => sv.key)
            .IsUnique();

        // Marketplace integration indexes
        builder.Entity<ProductMarketplaceLink>()
            .HasIndex(l => new { l.productId, l.connectionId })
            .IsUnique();

        builder.Entity<ProductMarketplaceLink>()
            .HasIndex(l => new { l.externalProductId, l.connectionId })
            .IsUnique();

        // Non-unique: multiple mappings allowed per (localCategoryId, connectionId)
        builder.Entity<CategoryMapping>()
            .HasIndex(m => new { m.localCategoryId, m.connectionId });

        // ProductMarketplaceLink → CategoryMapping (optional, ON DELETE SET NULL)
        builder.Entity<ProductMarketplaceLink>()
            .HasOne(l => l.categoryMapping)
            .WithMany()
            .HasForeignKey(l => l.categoryMappingId)
            .OnDelete(DeleteBehavior.SetNull);

        // Index for COUNT(*) per categoryMappingId in GetMappings — without this EF does a full table scan per mapping
        builder.Entity<ProductMarketplaceLink>()
            .HasIndex(l => l.categoryMappingId);

        // CategoryAttribute options - use LONGTEXT for large Ozon dictionaries
        builder.Entity<CategoryAttribute>()
            .Property(a => a.options)
            .HasColumnType("longtext");

        builder.Entity<CategoryAttribute>()
            .Property(a => a.isMultiline)
            .HasDefaultValue(false);

        builder.Entity<CategoryAttribute>()
            .Property(a => a.showOnProductPage)
            .HasDefaultValue(true);

        builder.Entity<CategoryAttribute>()
            .Property(a => a.descriptionInTooltip)
            .HasDefaultValue(false);

        // CategoryMapping graphJson - LONGTEXT for the whole-category visual node graph
        builder.Entity<CategoryMapping>()
            .Property(m => m.graphJson)
            .HasColumnType("longtext");

        builder.Entity<MappingGenerativeCache>()
            .HasIndex(c => new { c.mappingId, c.nodeId, c.productId, c.promptHash, c.seed, c.modelConfigId, c.inputHash })
            .IsUnique()
            .HasDatabaseName("IX_mapGenCache_lookup");

        // Product grouping indexes
        builder.Entity<ProductGroupMember>()
            .HasIndex(m => new { m.groupId, m.productId })
            .IsUnique();

        builder.Entity<ProductGroupMarketplaceLink>()
            .HasIndex(l => new { l.productGroupId, l.connectionId })
            .IsUnique();

        builder.Entity<ProductGroupMarketplaceLink>()
            .HasIndex(l => new { l.externalGroupId, l.connectionId })
            .IsUnique();

        // ProductVariant: index on productId
        builder.Entity<ProductVariant>()
            .HasIndex(v => v.productId);

        // ProductVariantLink: unique (variantId, connectionId), index on connectionId
        builder.Entity<ProductVariantLink>()
            .HasIndex(l => new { l.variantId, l.connectionId })
            .IsUnique();

        builder.Entity<ProductVariantLink>()
            .HasIndex(l => l.connectionId);

        // Pending product import indexes
        builder.Entity<PendingProductImport>()
            .HasIndex(p => p.batchId);

        builder.Entity<PendingProductImport>()
            .HasIndex(p => new { p.status, p.createdAt });

        // Pending product export indexes
        builder.Entity<PendingProductExport>()
            .HasIndex(p => p.batchId);

        builder.Entity<PendingProductExport>()
            .HasIndex(p => new { p.status, p.createdAt });

        // Pending product transfer indexes (background queue lookups)
        builder.Entity<PendingProductTransfer>()
            .HasIndex(p => p.batchId);

        builder.Entity<PendingProductTransfer>()
            .HasIndex(p => new { p.status, p.createdAt });

        // Foreign key indexes for query performance
        builder.Entity<Product>()
            .HasIndex(p => p.storeId);

        builder.Entity<Product>()
            .HasQueryFilter(p => p.deletedAt == null);

        builder.Entity<Product>()
            .HasIndex(p => new { p.storeId, p.deletedAt });

        builder.Entity<Category>()
            .HasIndex(c => c.storeId);

        builder.Entity<StoreAttribute>()
            .HasIndex(sa => sa.storeId);

        builder.Entity<StoreAttribute>()
            .Property(a => a.isMultiline)
            .HasDefaultValue(false);

        builder.Entity<StoreAttribute>()
            .Property(a => a.showOnProductPage)
            .HasDefaultValue(true);

        builder.Entity<StoreAttribute>()
            .Property(a => a.descriptionInTooltip)
            .HasDefaultValue(false);

        builder.Entity<ProductAttributeValue>()
            .HasIndex(pav => pav.productId);

        builder.Entity<ProductAttributeValue>()
            .HasIndex(pav => pav.attributeId);

        builder.Entity<ProductAttributeValue>()
            .Property(x => x.value)
            .HasColumnType("longtext");

        builder.Entity<StoreProductAttributeValue>()
            .HasIndex(spav => spav.productId);

        builder.Entity<Models.File>()
            .HasIndex(f => f.productId);

        builder.Entity<Models.File>()
            .HasIndex(f => f.folderId);

        builder.Entity<StoreFile>()
            .HasIndex(f => f.storeId);

        builder.Entity<StoreFile>()
            .HasIndex(f => f.folderId);

        // ProductFolder self-referencing relationship
        builder.Entity<ProductFolder>()
            .HasOne(f => f.parentFolder)
            .WithMany(f => f.childFolders)
            .HasForeignKey(f => f.parentFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        // File -> ProductFolder relationship
        builder.Entity<Models.File>()
            .HasOne(f => f.folder)
            .WithMany(f => f.files)
            .HasForeignKey(f => f.folderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<ProductFolder>()
            .HasIndex(pf => pf.productId);

        builder.Entity<ProductFolder>()
            .HasIndex(pf => pf.parentFolderId);

        builder.Entity<StoreFolder>()
            .HasOne(f => f.parentFolder)
            .WithMany(f => f.childFolders)
            .HasForeignKey(f => f.parentFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StoreFile>()
            .HasOne(f => f.folder)
            .WithMany(f => f.files)
            .HasForeignKey(f => f.folderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<StoreFolder>()
            .HasIndex(sf => sf.storeId);

        builder.Entity<StoreFolder>()
            .HasIndex(sf => sf.parentFolderId);

        builder.Entity<ImageReference>()
            .HasIndex(i => i.productId);

        builder.Entity<Note>()
            .HasIndex(n => n.productId);

        builder.Entity<ProductGroup>()
            .HasIndex(pg => pg.storeId);

        builder.Entity<MarketplaceConnection>()
            .HasIndex(mc => mc.storeId);

        builder.Entity<PendingImageDownload>()
            .HasIndex(pid => pid.productId);

        builder.Entity<PendingImageDownload>()
            .HasIndex(pid => new { pid.status, pid.storeId });

        // RefreshToken indexes
        builder.Entity<RefreshToken>()
            .HasIndex(rt => rt.token).IsUnique();
        builder.Entity<RefreshToken>()
            .HasIndex(rt => rt.userId);

        // FileShareToken indexes
        builder.Entity<FileShareToken>()
            .HasIndex(fst => fst.token).IsUnique();
        builder.Entity<FileShareToken>()
            .HasIndex(fst => fst.fileId);

        // FolderShareToken indexes
        builder.Entity<FolderShareToken>()
            .HasIndex(fst => fst.token).IsUnique();

        builder.Entity<StoreFileShareToken>()
            .HasIndex(fst => fst.token).IsUnique();

        builder.Entity<StoreFileShareToken>()
            .HasIndex(fst => fst.fileId);

        builder.Entity<StoreFolderShareToken>()
            .HasIndex(fst => fst.token).IsUnique();

        // TagDefinition indexes
        builder.Entity<TagDefinition>()
            .HasIndex(td => td.storeId);

        // FileTag: unique (fileId, tagDefinitionId) — one tag per file
        builder.Entity<FileTag>()
            .HasIndex(ft => new { ft.fileId, ft.tagDefinitionId })
            .IsUnique();

        builder.Entity<StoreFileTag>()
            .HasIndex(ft => new { ft.fileId, ft.tagDefinitionId })
            .IsUnique();

        // Operation indexes
        builder.Entity<Operation>()
            .HasIndex(o => o.operationId).IsUnique();

        builder.Entity<Operation>()
            .HasIndex(o => new { o.storeId, o.createdAt });

        // OperationLogEntry indexes
        builder.Entity<OperationLogEntry>()
            .HasIndex(e => e.operationId);

        // ErrorLog indexes
        builder.Entity<ErrorLog>()
            .HasIndex(e => e.createdAt);

        builder.Entity<ErrorLog>()
            .HasIndex(e => e.statusCode);

        // Adapter HTTP logs
        builder.Entity<AdapterHttpLog>()
            .HasIndex(l => new { l.storeId, l.createdAt });
        builder.Entity<AdapterHttpLog>()
            .HasIndex(l => new { l.connectionId, l.createdAt });
        builder.Entity<AdapterHttpLog>()
            .HasIndex(l => new { l.statusCode, l.createdAt });
        builder.Entity<AdapterHttpLog>()
            .HasIndex(l => new { l.adapterKey, l.createdAt });

        // AppSetting indexes
        builder.Entity<AppSetting>()
            .HasIndex(s => s.key)
            .IsUnique();

        builder.Entity<AppSetting>()
            .HasOne(s => s.updatedByUser)
            .WithMany()
            .HasForeignKey(s => s.updatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // AiModelConfig indexes
        builder.Entity<AiModelConfig>()
            .HasIndex(m => m.isDefault);

        // AiOperation indexes
        builder.Entity<AiOperation>()
            .HasIndex(o => new { o.userId, o.storeId, o.createdAt });

        // AiConversation — one session per sessionId
        builder.Entity<AiConversation>()
            .HasIndex(c => c.sessionId)
            .IsUnique();
        builder.Entity<AiConversation>()
            .HasIndex(c => new { c.storeId, c.userId });
        builder.Entity<AiConversationMessage>()
            .HasIndex(m => new { m.conversationId, m.createdAt });

        // AiStoreSettings — one row per store
        builder.Entity<AiStoreSettings>()
            .HasIndex(s => s.StoreId)
            .IsUnique();

        builder.Entity<StoreCurrency>()
            .HasIndex(c => new { c.storeId, c.code })
            .IsUnique();

        builder.Entity<StoreCurrency>()
            .HasOne(c => c.store)
            .WithMany(s => s.currencies)
            .HasForeignKey(c => c.storeId)
            .OnDelete(DeleteBehavior.Cascade);

        // One marketplace group per evaluated grouping value per connection
        builder.Entity<GroupValueMarketplaceLink>()
            .HasIndex(l => new { l.connectionId, l.groupValue })
            .IsUnique();
    }
}
