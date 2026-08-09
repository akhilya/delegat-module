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
    public DbSet<PendingProductBulkUpdate> pendingProductBulkUpdates { get; set; }
    public DbSet<PendingFolderJob> pendingFolderJobs { get; set; }
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
    public DbSet<UserApiKey> userApiKeys { get; set; }
    /// <summary>
    /// Legacy M2M - readable until DropStoreAdministrators applies. Excluded from further schema diffs.
    /// </summary>
    public DbSet<StoreAdministrator> storeAdministrators { get; set; }

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

    // Instance runtime settings (admin UI toggles)
    public DbSet<InstanceLoggingSettings> instanceLoggingSettings { get; set; }
    public DbSet<InstancePublicApiSettings> instancePublicApiSettings { get; set; }
    public DbSet<InstancePlatformEntitlements> instancePlatformEntitlements { get; set; }

    // Per-store white-label branding
    public DbSet<StoreBrandingSettings> storeBrandingSettings { get; set; }

    // Outbound marketplace HTTP request logs
    public DbSet<AdapterHttpLog> adapterHttpLogs { get; set; }

    // Inbound Public API request logs
    public DbSet<ApiRequestLog> apiRequestLogs { get; set; }

    public DbSet<AiModelConfig> aiModelConfigs { get; set; }
    public DbSet<AiOperation> aiOperations { get; set; }
    public DbSet<AiStoreSettings> aiStoreSettings { get; set; }
    public DbSet<AiConversation> aiConversations { get; set; }

    // Reference Catalog (synced from Control Plane)
    public DbSet<ReferenceCurrency> referenceCurrencies { get; set; }
    public DbSet<ReferenceTimezone> referenceTimezones { get; set; }
    public DbSet<ReferencePlugin> referencePlugins { get; set; }
    public DbSet<AiConversationMessage> aiConversationMessages { get; set; }

    public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Ignore<IdentityUserLogin<int>>();
        builder.Ignore<IdentityUserToken<int>>();
        builder.Entity<IdentityUserRole<int>>().HasKey(r => new { r.UserId, r.RoleId });

        // Kept queryable for MigrateLegacyAccessAsync between RolesAndPermissions and DropStoreAdministrators.
        // ExcludeFromMigrations: Drop is already scaffolded; do not recreate the table on future adds.
        builder.Entity<StoreAdministrator>(e =>
        {
            e.ToTable("StoreAdministrators", t => t.ExcludeFromMigrations());
            e.HasKey(x => new { x.StoreId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<User>()
            .HasOne(u => u.selectedStore)
            .WithMany()
            .HasForeignKey(u => u.selectedStoreId);

        // Auth: refresh tokens already configured elsewhere; API keys
        builder.Entity<UserApiKey>()
            .HasIndex(k => k.keyHash)
            .IsUnique();

        builder.Entity<UserApiKey>()
            .HasIndex(k => k.userId);

        builder.Entity<UserApiKey>()
            .HasOne(k => k.user)
            .WithMany()
            .HasForeignKey(k => k.userId)
            .OnDelete(DeleteBehavior.Cascade);

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

        // Index for COUNT(*) per categoryMappingId in GetMappings - without this EF does a full table scan per mapping
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

        builder.Entity<PendingProductBulkUpdate>()
            .HasIndex(p => p.batchId);

        builder.Entity<PendingProductBulkUpdate>()
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

        // FileTag: unique (fileId, tagDefinitionId) - one tag per file
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

        builder.Entity<ApiRequestLog>()
            .HasIndex(l => new { l.userId, l.createdAt });
        builder.Entity<ApiRequestLog>()
            .HasIndex(l => new { l.apiKeyId, l.createdAt });
        builder.Entity<ApiRequestLog>()
            .HasIndex(l => new { l.statusCode, l.createdAt });

        // AiModelConfig indexes
        builder.Entity<AiModelConfig>()
            .HasIndex(m => m.isDefault);

        // AiOperation indexes
        builder.Entity<AiOperation>()
            .HasIndex(o => new { o.userId, o.storeId, o.createdAt });

        // AiConversation - one session per sessionId
        builder.Entity<AiConversation>()
            .HasIndex(c => c.sessionId)
            .IsUnique();
        builder.Entity<AiConversation>()
            .HasIndex(c => new { c.storeId, c.userId });
        builder.Entity<AiConversationMessage>()
            .HasIndex(m => new { m.conversationId, m.createdAt });

        // AiStoreSettings - one row per store
        builder.Entity<AiStoreSettings>()
            .HasIndex(s => s.StoreId)
            .IsUnique();

        // StoreBrandingSettings - one row per store
        builder.Entity<StoreBrandingSettings>()
            .HasIndex(s => s.StoreId)
            .IsUnique();
        builder.Entity<StoreBrandingSettings>()
            .HasOne(s => s.Store)
            .WithMany()
            .HasForeignKey(s => s.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

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

        // Folder job queue: polled every few seconds by status + createdAt
        builder.Entity<PendingFolderJob>()
            .Property(j => j.operationId)
            .HasMaxLength(64);
        builder.Entity<PendingFolderJob>()
            .HasIndex(j => new { j.status, j.createdAt });
        builder.Entity<PendingFolderJob>()
            .HasIndex(j => j.storeId);
        builder.Entity<PendingFolderJob>()
            .HasIndex(j => j.operationId);
    }
}
