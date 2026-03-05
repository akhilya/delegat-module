using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Products;

namespace PrintO;

public class DataContext : IdentityDbContext<User, UserRole, int>
{
    public DbSet<Store> stores { get; set; }
    public DbSet<Product> products { get; set; }
    public DbSet<Models.File> files { get; set; }
    public DbSet<ImageReference> images { get; set; }
    public DbSet<PendingImageDownload> pendingImageDownloads { get; set; }
    public DbSet<PendingProductImport> pendingProductImports { get; set; }
    public DbSet<PendingProductExport> pendingProductExports { get; set; }
    public DbSet<InvitationToken> invitationTokens { get; set; }
    public DbSet<Note> notes { get; set; }
    public DbSet<FileTag> tags { get; set; }
    public DbSet<ProductFolder> productFolders { get; set; }

    public DbSet<Category> categories { get; set; }
    public DbSet<CategoryAttribute> categoryAttributes { get; set; }
    public DbSet<ProductAttributeValue> productAttributeValues { get; set; }

    public DbSet<StoreAttribute> storeAttributes { get; set; }
    public DbSet<StoreProductAttributeValue> storeProductAttributeValues { get; set; }

    // Marketplace integration
    public DbSet<MarketplaceConnection> marketplaceConnections { get; set; }
    public DbSet<CategoryMapping> categoryMappings { get; set; }
    public DbSet<FieldMapping> fieldMappings { get; set; }
    public DbSet<ProductMarketplaceLink> productMarketplaceLinks { get; set; }

    // System versioning
    public DbSet<SystemVersion> systemVersions { get; set; }

    // Product variants (sizes)
    public DbSet<ProductVariant> productVariants { get; set; }
    public DbSet<ProductVariantLink> productVariantLinks { get; set; }

    // Product grouping
    public DbSet<ProductGroup> productGroups { get; set; }
    public DbSet<ProductGroupMember> productGroupMembers { get; set; }
    public DbSet<ProductGroupMarketplaceLink> productGroupMarketplaceLinks { get; set; }

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

        // CategoryAttribute options - use LONGTEXT for large Ozon dictionaries
        builder.Entity<CategoryAttribute>()
            .Property(a => a.options)
            .HasColumnType("longtext");

        // FieldMapping modifiersJson - LONGTEXT for JSON modifier chains
        builder.Entity<FieldMapping>()
            .Property(fm => fm.modifiersJson)
            .HasColumnType("longtext");

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

        // Foreign key indexes for query performance
        builder.Entity<Product>()
            .HasIndex(p => p.storeId);

        builder.Entity<Category>()
            .HasIndex(c => c.storeId);

        builder.Entity<StoreAttribute>()
            .HasIndex(sa => sa.storeId);

        builder.Entity<ProductAttributeValue>()
            .HasIndex(pav => pav.productId);

        builder.Entity<ProductAttributeValue>()
            .HasIndex(pav => pav.attributeId);

        builder.Entity<StoreProductAttributeValue>()
            .HasIndex(spav => spav.productId);

        builder.Entity<Models.File>()
            .HasIndex(f => f.productId);

        builder.Entity<Models.File>()
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
    }
}
