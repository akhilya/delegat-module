using System.Data.Common;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Delegat.Infrastructure.Data;

/// <summary>
/// Enforces a shared product/variant barcode namespace on every tracked catalog write.
/// MySQL store-row locks serialize validation and persistence, including across API processes.
/// Trashed products retain their barcodes. Existing duplicates are not silently rewritten.
/// </summary>
internal static class BarcodeUniqueness
{
    private sealed record Claim(int StoreId, string Barcode, bool Variant, int Id);

    public static async Task<int> SaveAsync(DataContext db, Func<bool, Task<int>> save,
        bool acceptAllChangesOnSuccess, CancellationToken ct)
    {
        var productEntries = db.ChangeTracker.Entries<Product>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Deleted ||
                e.State == EntityState.Modified &&
                (e.Property(p => p.barcode).IsModified || e.Property(p => p.storeId).IsModified))
            .ToList();
        var variantEntries = db.ChangeTracker.Entries<ProductVariant>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Deleted ||
                e.State == EntityState.Modified &&
                (e.Property(v => v.barcode).IsModified || e.Property(v => v.productId).IsModified))
            .ToList();
        if (productEntries.Count == 0 && variantEntries.Count == 0)
            return await save(acceptAllChangesOnSuccess);

        var changedProducts = productEntries.Select(e => e.Property(p => p.Id).CurrentValue).ToHashSet();
        var changedVariants = variantEntries.Select(e => e.Property(v => v.Id).CurrentValue).ToHashSet();
        var claims = new List<Claim>();
        foreach (var entry in productEntries.Where(e => e.State != EntityState.Deleted))
        {
            var product = entry.Entity;
            product.barcode = Normalize(product.barcode);
            if (product.barcode != null)
                claims.Add(new(entry.Property(p => p.storeId).CurrentValue, product.barcode, false, product.Id));
        }

        var parentIds = variantEntries.Select(e => e.Property(v => v.productId).CurrentValue).Distinct().ToList();
        var parents = await db.products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => parentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.storeId, ct);
        var expectedStores = new Dictionary<int, int>(parents);
        foreach (var entry in productEntries.Where(e => e.State != EntityState.Added))
            expectedStores[entry.Entity.Id] = entry.Property(p => p.storeId).OriginalValue;
        foreach (var entry in productEntries)
            parents[entry.Property(p => p.Id).CurrentValue] = entry.Property(p => p.storeId).CurrentValue;

        foreach (var entry in variantEntries.Where(e => e.State != EntityState.Deleted))
        {
            var variant = entry.Entity;
            variant.barcode = Normalize(variant.barcode);
            if (variant.barcode != null && parents.TryGetValue(entry.Property(v => v.productId).CurrentValue, out var storeId))
                claims.Add(new(storeId, variant.barcode, true, variant.Id));
        }

        // A store transfer also transfers variants whose own properties did not change.
        var movedProducts = productEntries.Where(e => e.State == EntityState.Modified &&
            e.Property(p => p.storeId).IsModified).ToDictionary(e => e.Entity.Id, e => e.Entity.storeId);
        if (claims.Count == 0 && movedProducts.Count == 0)
            return await save(acceptAllChangesOnSuccess);

        async Task<int> ValidateAndSave(bool accept)
        {
            var effectiveClaims = new List<Claim>(claims);
            var effectiveChangedVariants = new HashSet<int>(changedVariants);
            if (db.Database.IsRelational())
            {
                foreach (var storeId in claims.Select(c => c.StoreId).Concat(expectedStores.Values)
                    .Concat(movedProducts.Values).Distinct().Order())
                    await LockStoreAsync(db, storeId, ct);
                await CheckParentStoresAsync(db, expectedStores, ct);
            }
            if (movedProducts.Count > 0)
            {
                foreach (var variant in await ReadMovedVariantsAsync(db, movedProducts, ct))
                {
                    if (!effectiveChangedVariants.Add(variant.Id)) continue;
                    effectiveClaims.Add(variant);
                }
            }
            foreach (var group in effectiveClaims.GroupBy(c => (c.StoreId, c.Barcode)))
                if (group.Count() > 1)
                    throw Conflict();

            foreach (var storeClaims in effectiveClaims.GroupBy(c => c.StoreId).OrderBy(g => g.Key))
            {
                var codes = storeClaims.Select(c => c.Barcode).Distinct().ToList();
                if (db.Database.IsRelational())
                {
                    // Current (locking) reads are essential under MySQL REPEATABLE READ:
                    // an outer transaction may already have an older consistent snapshot.
                    await CheckDatabaseAsync(db, storeClaims.Key, codes, changedProducts, effectiveChangedVariants, ct);
                }
                else
                {
                    if (await db.products.IgnoreQueryFilters().AsNoTracking().AnyAsync(p =>
                            p.storeId == storeClaims.Key && p.barcode != null && codes.Contains(p.barcode) &&
                            !changedProducts.Contains(p.Id), ct) ||
                        await db.productVariants.IgnoreQueryFilters().AsNoTracking().AnyAsync(v =>
                            v.product.storeId == storeClaims.Key && v.barcode != null && codes.Contains(v.barcode) &&
                            !effectiveChangedVariants.Contains(v.Id), ct))
                        throw Conflict();
                }
            }
            return await save(accept);
        }

        if (!db.Database.IsRelational() || db.Database.CurrentTransaction != null)
            return await ValidateAndSave(acceptAllChangesOnSuccess);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await ValidateAndSave(false);
            await transaction.CommitAsync(ct);
            if (acceptAllChangesOnSuccess)
                db.ChangeTracker.AcceptAllChanges();
            return result;
        });
    }

    private static string? Normalize(string? barcode) =>
        string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();

    private static ApiException Conflict() => ApiException.Conflict(
        "Barcode is already assigned to a product or variant in this store.", code: "barcode_already_used");

    private static DbCommand Command(DataContext db, string sql)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        command.CommandTimeout = db.Database.GetCommandTimeout() ?? 30;
        return command;
    }

    private static void Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task LockStoreAsync(DataContext db, int storeId, CancellationToken ct)
    {
        await using var command = Command(db, "SELECT `Id` FROM `stores` WHERE `Id` = @storeId FOR UPDATE");
        Parameter(command, "@storeId", storeId);
        await command.ExecuteScalarAsync(ct);
    }

    private static async Task CheckParentStoresAsync(DataContext db, Dictionary<int, int> expectedStores,
        CancellationToken ct)
    {
        if (expectedStores.Count == 0) return;
        var placeholders = string.Join(",", expectedStores.Keys.Select((_, i) => $"@id{i}"));
        await using var command = Command(db,
            $"SELECT `Id`, `storeId` FROM `products` WHERE `Id` IN ({placeholders}) FOR UPDATE");
        var index = 0;
        foreach (var id in expectedStores.Keys)
            Parameter(command, $"@id{index++}", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (reader.GetInt32(1) != expectedStores[reader.GetInt32(0)])
                throw ApiException.Conflict(); // Parent moved after the request loaded it; reload before retrying.
    }

    private static async Task<List<Claim>> ReadMovedVariantsAsync(DataContext db,
        Dictionary<int, int> movedProducts, CancellationToken ct)
    {
        var result = new List<Claim>();
        if (!db.Database.IsRelational())
        {
            var ids = movedProducts.Keys.ToList();
            foreach (var variant in await db.productVariants.AsNoTracking()
                .Where(v => ids.Contains(v.productId)).ToListAsync(ct))
                if (Normalize(variant.barcode) is { } barcode)
                    result.Add(new(movedProducts[variant.productId], barcode, true, variant.Id));
            return result;
        }
        var placeholders = string.Join(",", movedProducts.Keys.Select((_, i) => $"@id{i}"));
        await using var command = Command(db,
            $"SELECT `Id`, `productId`, `barcode` FROM `productVariants` WHERE `productId` IN ({placeholders}) FOR UPDATE");
        var index = 0;
        foreach (var id in movedProducts.Keys)
            Parameter(command, $"@id{index++}", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (!reader.IsDBNull(2) && Normalize(reader.GetString(2)) is { } barcode)
                result.Add(new(movedProducts[reader.GetInt32(1)], barcode, true, reader.GetInt32(0)));
        return result;
    }

    private static async Task CheckDatabaseAsync(DataContext db, int storeId, List<string> codes,
        HashSet<int> changedProducts, HashSet<int> changedVariants, CancellationToken ct)
    {
        var placeholders = string.Join(",", codes.Select((_, i) => $"@code{i}"));
        // Separate current reads avoid a UNION's non-locking consistent-read semantics.
        foreach (var variant in new[] { false, true })
        {
            var sql = variant
                ? $"SELECT v.`Id` FROM `productVariants` v JOIN `products` p ON p.`Id` = v.`productId` WHERE p.`storeId` = @storeId AND v.`barcode` IN ({placeholders}) FOR UPDATE"
                : $"SELECT p.`Id` FROM `products` p WHERE p.`storeId` = @storeId AND p.`barcode` IN ({placeholders}) FOR UPDATE";
            await using var command = Command(db, sql);
            Parameter(command, "@storeId", storeId);
            for (var i = 0; i < codes.Count; i++)
                Parameter(command, $"@code{i}", codes[i]);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var changed = variant ? changedVariants : changedProducts;
            while (await reader.ReadAsync(ct))
                if (!changed.Contains(reader.GetInt32(0)))
                    throw Conflict();
        }
    }
}
