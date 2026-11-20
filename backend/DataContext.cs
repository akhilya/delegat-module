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
    public DbSet<ProductMediaExportGrant> productMediaExportGrants { get; set; }
    public DbSet<ImageReference> images { get; set; }
    public DbSet<PendingImageDownload> pendingImageDownloads { get; set; }
    public DbSet<PendingProductImport> pendingProductImports { get; set; }
    public DbSet<PendingProductExport> pendingProductExports { get; set; }
    public DbSet<PendingProductTransfer> pendingProductTransfers { get; set; }
    public DbSet<PendingProductBulkUpdate> pendingProductBulkUpdates { get; set; }
    public DbSet<PendingFolderJob> pendingFolderJobs { get; set; }
    public DbSet<PendingCatalogConnectJob> pendingCatalogConnectJobs { get; set; }
    public DbSet<PendingCategoryImport> pendingCategoryImports { get; set; }
    public DbSet<ScheduledTask> scheduledTasks { get; set; }
    public DbSet<ScheduledTaskRun> scheduledTaskRuns { get; set; }
    public DbSet<InvitationToken> invitationTokens { get; set; }
    public DbSet<Note> notes { get; set; }
    public DbSet<FileTag> tags { get; set; }
    public DbSet<TagDefinition> tagDefinitions { get; set; }
    public DbSet<ProductFolder> productFolders { get; set; }

    public DbSet<Category> categories { get; set; }
    public DbSet<CategoryAttribute> categoryAttributes { get; set; }
    public DbSet<ProductAttributeValue> productAttributeValues { get; set; }
    public DbSet<ProductAttributeFile> productAttributeFiles { get; set; }

    public DbSet<StoreAttribute> storeAttributes { get; set; }
    public DbSet<StoreProductAttributeValue> storeProductAttributeValues { get; set; }
    public DbSet<StoreProductAttributeFile> storeProductAttributeFiles { get; set; }

    // Marketplace integration
    public DbSet<MarketplaceConnection> marketplaceConnections { get; set; }
    public DbSet<CategoryMapping> categoryMappings { get; set; }
    public DbSet<MappingGenerativeCache> mappingGenerativeCaches { get; set; }
    public DbSet<ProductMarketplaceLink> productMarketplaceLinks { get; set; }
    public DbSet<MarketplaceOrder> marketplaceOrders { get; set; }
    public DbSet<MarketplaceOrderSyncState> marketplaceOrderSyncStates { get; set; }

    // Durable integration executor (V1)
    public DbSet<Models.Integration.IntegrationCommand> integrationCommands { get; set; }
    public DbSet<Models.Integration.IntegrationStage> integrationStages { get; set; }
    public DbSet<Models.Integration.IntegrationAttempt> integrationAttempts { get; set; }
    public DbSet<Models.Integration.IntegrationScopeState> integrationScopeStates { get; set; }

    // Auth
    public DbSet<RefreshToken> refreshTokens { get; set; }
    public DbSet<UserApiKey> userApiKeys { get; set; }
    public DbSet<WebAuthnCredential> webAuthnCredentials { get; set; }
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
    public DbSet<AiTurnJob> aiTurnJobs { get; set; }
    public DbSet<AiTurnJournalEvent> aiTurnJournalEvents { get; set; }
    public DbSet<AiKnowledgeChunk> aiKnowledgeChunks { get; set; }
    public DbSet<AiSandboxRun> aiSandboxRuns { get; set; }
    public DbSet<AiAgentAudit> aiAgentAudits { get; set; }
    public DbSet<AiChatAttachment> aiChatAttachments { get; set; }
    public DbSet<AiGeneratedArtifact> aiGeneratedArtifacts { get; set; }
    public DbSet<AiGeneratedArtifactImport> aiGeneratedArtifactImports { get; set; }
    public DbSet<AiMcpServer> aiMcpServers { get; set; }
    public DbSet<AiMemory> aiMemories { get; set; }

    public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Ignore<IdentityUserLogin<int>>();
        builder.Entity<IdentityUserRole<int>>().HasKey(r => new { r.UserId, r.RoleId });
        builder.Entity<IdentityUserToken<int>>(e =>
        {
            e.ToTable("UserTokens");
            e.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
            e.Property(t => t.LoginProvider).HasMaxLength(128);
            e.Property(t => t.Name).HasMaxLength(128);
        });

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

        builder.Entity<WebAuthnCredential>(e =>
        {
            e.ToTable("webAuthnCredentials");
            e.Property(c => c.credentialId).IsRequired().HasMaxLength(1024);
            e.Property(c => c.publicKey).IsRequired();
            e.Property(c => c.userHandle).IsRequired();
            e.Property(c => c.nickname).HasMaxLength(128);
            e.HasIndex(c => c.credentialId).IsUnique();
            e.HasIndex(c => c.userId);
            e.HasOne(c => c.user)
                .WithMany()
                .HasForeignKey(c => c.userId)
                .OnDelete(DeleteBehavior.Cascade);
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

        // Index for COUNT(*) per categoryMappingId in GetMappings - without this EF does a full table scan per mapping
        builder.Entity<ProductMarketplaceLink>()
            .HasIndex(l => l.categoryMappingId);

        // Durable integration executor (V1)
        builder.Entity<Models.Integration.IntegrationCommand>(e =>
        {
            e.HasIndex(c => c.commandId).IsUnique();
            e.HasIndex(c => c.operationId);
            // Request keys deduplicate inside one admission scope; multiple NULLs allowed
            e.HasIndex(c => new { c.scopeKey, c.requestKey }).IsUnique();
            e.HasIndex(c => new { c.storeId, c.state });
            e.Property(c => c.commandId).HasMaxLength(64);
            e.Property(c => c.operationId).HasMaxLength(64);
            e.Property(c => c.scopeKey).HasMaxLength(200);
            e.Property(c => c.kind).HasMaxLength(50);
            e.Property(c => c.requestKey).HasMaxLength(200);
            e.Property(c => c.requestHash).HasMaxLength(64);
            e.Property(c => c.state).HasMaxLength(20);
            e.Property(c => c.finalizedBy).HasMaxLength(100);
            e.Property(c => c.inputJson).HasColumnType("longtext");
            e.Property(c => c.summary).HasColumnType("text");
        });

        builder.Entity<Models.Integration.IntegrationStage>(e =>
        {
            e.HasIndex(s => new { s.commandId, s.itemKey, s.stageKey }).IsUnique();
            // Worker polling: due ready/deferred stages and confirmation follow-ups
            e.HasIndex(s => new { s.state, s.nextCheckAt });
            e.HasIndex(s => s.claimedUntil);
            e.Property(s => s.itemKey).HasMaxLength(200);
            e.Property(s => s.stageKey).HasMaxLength(200);
            e.Property(s => s.kind).HasMaxLength(50);
            e.Property(s => s.batchKey).HasMaxLength(200);
            e.Property(s => s.state).HasMaxLength(20);
            e.Property(s => s.claimedBy).HasMaxLength(100);
            e.Property(s => s.payloadJson).HasColumnType("longtext");
            e.Property(s => s.payloadFileKey).HasMaxLength(500);
            e.Property(s => s.dependsOnJson).HasColumnType("text");
            e.Property(s => s.receiptJson).HasColumnType("text");
            e.Property(s => s.evidenceJson).HasColumnType("text");
            e.Property(s => s.issuesJson).HasColumnType("text");
            e.HasOne(s => s.command)
                .WithMany()
                .HasForeignKey(s => s.commandId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Models.Integration.IntegrationAttempt>(e =>
        {
            e.HasIndex(a => a.requestKey).IsUnique();
            e.HasIndex(a => new { a.stageId, a.attemptNumber }).IsUnique();
            e.HasIndex(a => a.commandId);
            e.HasIndex(a => a.dispatchId);
            e.Property(a => a.stageKey).HasMaxLength(200);
            e.Property(a => a.dispatchId).HasMaxLength(64);
            e.Property(a => a.requestKey).HasMaxLength(200);
            e.Property(a => a.requestHash).HasMaxLength(64);
            e.Property(a => a.outcome).HasMaxLength(30);
            e.Property(a => a.effectCertainty).HasMaxLength(20);
            e.Property(a => a.responseSummary).HasColumnType("text");
            e.Property(a => a.issuesJson).HasColumnType("text");
        });

        builder.Entity<Models.Integration.IntegrationScopeState>(e =>
        {
            e.HasKey(s => s.scopeKey);
            e.Property(s => s.scopeKey).HasMaxLength(64);
            e.Property(s => s.leaseOwner).HasMaxLength(32);
        });

        // CategoryAttribute options - use LONGTEXT for large Ozon dictionaries
        builder.Entity<CategoryAttribute>()
            .Property(a => a.options)
            .HasColumnType("longtext");

        builder.Entity<CategoryAttribute>()
            .Property(a => a.isMultiline)
            .HasDefaultValue(false);

        builder.Entity<CategoryAttribute>()
            .Property(a => a.allowCustomValues)
            .HasDefaultValue(false);

        builder.Entity<CategoryAttribute>()
            .Property(a => a.showOnProductPage)
            .HasDefaultValue(true);

        builder.Entity<CategoryAttribute>()
            .Property(a => a.descriptionInTooltip)
            .HasDefaultValue(false);

        builder.Entity<CategoryAttribute>()
            .Property(a => a.minValue)
            .HasPrecision(18, 6);

        builder.Entity<CategoryAttribute>()
            .Property(a => a.maxValue)
            .HasPrecision(18, 6);

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

        // Clone defaults to copying files; DB default must match CLR default on the entity.
        builder.Entity<PendingProductTransfer>()
            .Property(p => p.copyFiles)
            .HasDefaultValue(true);

        builder.Entity<PendingProductBulkUpdate>()
            .HasIndex(p => p.batchId);

        builder.Entity<PendingProductBulkUpdate>()
            .HasIndex(p => new { p.status, p.createdAt });

        builder.Entity<PendingProductBulkUpdate>()
            .Property(p => p.processedCount).HasDefaultValue(-1).HasSentinel(-1).IsConcurrencyToken();

        // Foreign key indexes for query performance
        builder.Entity<Product>()
            .HasIndex(p => p.storeId);

        builder.Entity<Product>()
            .HasQueryFilter(p => p.deletedAt == null);

        builder.Entity<ProductImportReceipt>()
            .HasOne(r => r.product).WithMany().HasForeignKey(r => r.productId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Product>()
            .HasIndex(p => new { p.storeId, p.deletedAt });

        builder.Entity<Product>()
            .HasIndex(p => new { p.storeId, p.deletedAt, p.categoryId, p.Id });

        // Barcode validation performs locking equality reads on these columns.
        builder.Entity<Product>()
            .HasIndex(p => new { p.storeId, p.barcode });
        builder.Entity<ProductVariant>()
            .HasIndex(v => new { v.barcode, v.productId });

        builder.Entity<Category>()
            .HasIndex(c => c.storeId);

        builder.Entity<StoreAttribute>()
            .HasIndex(sa => sa.storeId);

        builder.Entity<StoreAttribute>()
            .Property(a => a.isMultiline)
            .HasDefaultValue(false);

        builder.Entity<StoreAttribute>()
            .Property(a => a.allowCustomValues)
            .HasDefaultValue(false);

        builder.Entity<StoreAttribute>()
            .Property(a => a.showOnProductPage)
            .HasDefaultValue(true);

        builder.Entity<StoreAttribute>()
            .Property(a => a.descriptionInTooltip)
            .HasDefaultValue(false);

        builder.Entity<StoreAttribute>()
            .Property(a => a.minValue)
            .HasPrecision(18, 6);

        builder.Entity<StoreAttribute>()
            .Property(a => a.maxValue)
            .HasPrecision(18, 6);

        builder.Entity<ProductAttributeValue>()
            .HasIndex(pav => pav.productId);

        builder.Entity<ProductAttributeValue>()
            .HasIndex(pav => pav.attributeId);

        builder.Entity<ProductAttributeValue>()
            .Property(x => x.value)
            .HasColumnType("longtext");

        builder.Entity<PendingImageDownload>()
            .Property(x => x.mediaKind)
            .HasDefaultValue(PendingMediaKind.Image);
        builder.Entity<PendingImageDownload>()
            .Property(x => x.skipGallery)
            .HasDefaultValue(false);

        builder.Entity<ProductAttributeFile>()
            .HasIndex(x => new { x.attributeValueId, x.fileId })
            .IsUnique();
        builder.Entity<ProductAttributeFile>()
            .HasIndex(x => new { x.attributeValueId, x.index })
            .IsUnique();
        builder.Entity<ProductAttributeFile>()
            .HasOne(x => x.attributeValue)
            .WithMany(x => x.files)
            .HasForeignKey(x => x.attributeValueId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProductAttributeFile>()
            .HasOne(x => x.file)
            .WithMany()
            .HasForeignKey(x => x.fileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StoreProductAttributeValue>()
            .HasIndex(spav => spav.productId);

        builder.Entity<StoreProductAttributeFile>()
            .HasIndex(x => new { x.attributeValueId, x.fileId })
            .IsUnique();
        builder.Entity<StoreProductAttributeFile>()
            .HasIndex(x => new { x.attributeValueId, x.index })
            .IsUnique();
        builder.Entity<StoreProductAttributeFile>()
            .HasOne(x => x.attributeValue)
            .WithMany(x => x.files)
            .HasForeignKey(x => x.attributeValueId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<StoreProductAttributeFile>()
            .HasOne(x => x.file)
            .WithMany()
            .HasForeignKey(x => x.fileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Models.File>()
            .HasIndex(f => f.productId);

        builder.Entity<Models.File>()
            .HasIndex(f => f.folderId);

        builder.Entity<Models.File>()
            .Property(f => f.sourceUrl)
            .HasMaxLength(Models.File.SourceUrlMaxLength);

        builder.Entity<ProductMediaExportGrant>()
            .HasIndex(g => g.token)
            .IsUnique();
        builder.Entity<ProductMediaExportGrant>()
            .HasIndex(g => new { g.fileId, g.connectionId })
            .IsUnique();
        builder.Entity<ProductMediaExportGrant>()
            .HasOne(g => g.file)
            .WithMany()
            .HasForeignKey(g => g.fileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProductMediaExportGrant>()
            .HasOne(g => g.connection)
            .WithMany()
            .HasForeignKey(g => g.connectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProductMediaExportGrant>()
            .Property(g => g.revision)
            .IsConcurrencyToken();

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
        builder.Entity<Note>()
            .HasIndex(n => n.authorUserId);
        builder.Entity<Note>()
            .HasIndex(n => n.createdAt);

        builder.Entity<ProductGroup>()
            .HasIndex(pg => pg.storeId);

        builder.Entity<MarketplaceConnection>()
            .HasIndex(mc => mc.storeId);

        builder.Entity<PendingImageDownload>()
            .HasIndex(pid => pid.productId);

        builder.Entity<PendingImageDownload>()
            .HasIndex(pid => new { pid.status, pid.storeId });

        builder.Entity<PendingImageDownload>()
            .Property(pid => pid.batchId).HasColumnType("longtext");
        builder.Entity<PendingImageDownload>()
            .HasIndex(pid => pid.batchId).HasPrefixLength(191);
        builder.Entity<PendingImageDownload>()
            .HasIndex(pid => new { pid.status, pid.createdAt });

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

        builder.Entity<OperationLogEntry>()
            .HasIndex(e => e.timestamp);

        // ErrorLog indexes
        builder.Entity<ErrorLog>()
            .HasIndex(e => e.createdAt);

        builder.Entity<ErrorLog>()
            .HasIndex(e => e.statusCode);

        // Adapter HTTP logs
        builder.Entity<AdapterHttpLog>()
            .HasIndex(l => new { l.storeId, l.createdAt });
        builder.Entity<AdapterHttpLog>()
            .HasIndex(l => new { l.storeId, l.statusCode });
        builder.Entity<AdapterHttpLog>()
            .HasIndex(l => l.createdAt);
        builder.Entity<ApiRequestLog>()
            .HasIndex(l => l.createdAt);
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
        builder.Entity<AiConversationMessage>()
            .Property(m => m.toolCallsJson)
            .IsConcurrencyToken();
        builder.Entity<AiTurnJob>().HasIndex(j => j.runId).IsUnique();
        builder.Entity<AiTurnJob>().HasIndex(j => j.activeSessionId).IsUnique();
        builder.Entity<AiTurnJob>().HasIndex(j => new { j.status, j.leaseExpiresAtUtc, j.Id });
        builder.Entity<AiTurnJob>().HasIndex(j => new { j.sessionId, j.storeId, j.userId, j.Id });
        builder.Entity<AiTurnJob>().Property(j => j.revision).IsConcurrencyToken();
        builder.Entity<AiTurnJob>().Property(j => j.leaseOwner).IsConcurrencyToken();
        builder.Entity<AiTurnJob>().HasOne(j => j.conversation).WithMany()
            .HasForeignKey(j => j.conversationId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AiTurnJournalEvent>().HasKey(e => new { e.jobId, e.sequence });
        builder.Entity<AiTurnJournalEvent>().HasOne(e => e.job).WithMany()
            .HasForeignKey(e => e.jobId).OnDelete(DeleteBehavior.Cascade);

        // AiStoreSettings - one row per store
        builder.Entity<AiStoreSettings>()
            .HasIndex(s => s.StoreId)
            .IsUnique();

        builder.Entity<AiKnowledgeChunk>()
            .HasIndex(c => c.sourcePath);
        builder.Entity<AiSandboxRun>()
            .HasIndex(r => new { r.storeId, r.createdAt });
        builder.Entity<AiAgentAudit>()
            .HasIndex(a => new { a.storeId, a.createdAt });
        builder.Entity<AiChatAttachment>()
            .HasIndex(a => new { a.storeId, a.userId, a.sessionId });
        builder.Entity<AiGeneratedArtifact>()
            .HasIndex(a => new { a.storeId, a.userId, a.expiresAtUtc });
        builder.Entity<AiGeneratedArtifactImport>()
            .HasIndex(i => new { i.storeId, i.userId, i.state });
        builder.Entity<AiGeneratedArtifactImport>()
            .HasIndex(i => i.artifactId);
        builder.Entity<AiMcpServer>()
            .HasIndex(s => s.isEnabled);
        builder.Entity<AiMemory>()
            .HasIndex(m => new { m.storeId, m.userId, m.category, m.key })
            .IsUnique();
        builder.Entity<AiMemory>()
            .HasIndex(m => new { m.storeId, m.userId });

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

        builder.Entity<PendingCatalogConnectJob>()
            .Property(j => j.operationId)
            .HasMaxLength(PendingCatalogConnectJob.OPERATION_ID_MAX_LENGTH);
        builder.Entity<PendingCatalogConnectJob>()
            .Property(j => j.mode)
            .HasMaxLength(PendingCatalogConnectJob.MODE_MAX_LENGTH);
        builder.Entity<PendingCatalogConnectJob>()
            .HasIndex(j => new { j.status, j.createdAt });
        builder.Entity<PendingCatalogConnectJob>()
            .HasIndex(j => j.storeId);
        builder.Entity<PendingCatalogConnectJob>()
            .HasIndex(j => j.operationId)
            .IsUnique();
        builder.Entity<PendingCatalogConnectJob>()
            .HasIndex(j => new { j.connectionId, j.kind, j.status });

        builder.Entity<PendingCategoryImport>()
            .Property(j => j.operationId)
            .HasMaxLength(PendingCategoryImport.OPERATION_ID_MAX_LENGTH);
        builder.Entity<PendingCategoryImport>()
            .HasIndex(j => new { j.status, j.createdAt });
        builder.Entity<PendingCategoryImport>()
            .HasIndex(j => j.operationId);
        builder.Entity<PendingCategoryImport>()
            .HasIndex(j => new { j.connectionId, j.externalCategoryId, j.status });

        builder.Entity<ScheduledTask>(e =>
        {
            e.Property(x => x.name).HasMaxLength(120);
            e.Property(x => x.actionId).HasMaxLength(160);
            e.Property(x => x.timeZoneId).HasMaxLength(100);
            e.HasIndex(x => new { x.isEnabled, x.nextRunAt });
            e.HasIndex(x => new { x.storeId, x.createdAt });
        });
        builder.Entity<ScheduledTaskRun>(e =>
        {
            e.Property(x => x.actionId).HasMaxLength(160);
            e.Property(x => x.status).HasMaxLength(24);
            e.HasIndex(x => new { x.scheduledTaskId, x.startedAt });
            e.HasIndex(x => new { x.storeId, x.startedAt });
            e.HasOne<ScheduledTask>()
                .WithMany()
                .HasForeignKey(x => x.scheduledTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PendingProductImport>()
            .HasIndex(p => new { p.connectionId, p.externalCategoryId });

        builder.Entity<MarketplaceOrder>(e =>
        {
            e.Property(x => x.channel).HasDefaultValue("shop");
            e.Property(x => x.total).HasPrecision(18, 2).HasColumnType("decimal(18,2)");
            e.HasIndex(x => new { x.storeId, x.connectionId, x.marketplaceOrderId }).IsUnique();
            e.HasIndex(x => new { x.storeId, x.status });
            e.HasIndex(x => new { x.connectionId, x.status });
            e.HasIndex(x => x.displayId);
            e.HasOne<MarketplaceConnection>()
                .WithMany()
                .HasForeignKey(x => x.connectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<MarketplaceOrderSyncState>(e =>
        {
            e.HasIndex(x => new { x.connectionId, x.status }).IsUnique();
            e.HasOne<MarketplaceConnection>()
                .WithMany()
                .HasForeignKey(x => x.connectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        TouchMappingGraphs();
        return await Infrastructure.Data.BarcodeUniqueness.SaveAsync(
            this,
            accept => base.SaveChangesAsync(accept, cancellationToken),
            acceptAllChangesOnSuccess,
            cancellationToken);
    }

    /// <summary>
    /// A graph write is a new wiring. Later export results are the only ones
    /// that still describe this mapping.
    /// </summary>
    private void TouchMappingGraphs()
    {
        foreach (var entry in ChangeTracker.Entries<CategoryMapping>())
        {
            var graph = entry.Property(m => m.graphJson);
            var changed = entry.State == EntityState.Added
                ? entry.Entity.graphJson != null
                : entry.State == EntityState.Modified && graph.IsModified;
            if (changed)
                entry.Entity.graphUpdatedAt = DateTime.UtcNow;
        }
    }
}
