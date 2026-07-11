using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;

namespace Delegat.Services.DataMigrations;

/// <summary>
/// Discovers and runs data migrations that haven't been applied yet.
/// Each migration runs in its own transaction for atomicity.
/// </summary>
public static class DataMigrationRunner
{
    public static async Task RunAsync(IServiceProvider rootProvider)
    {
        var logger = rootProvider.GetRequiredService<ILogger<DataContext>>();
        logger.LogInformation("Running platform data migrations...");

        // Discover all IDataMigration implementations in this assembly
        var migrationTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IDataMigration).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

        if (migrationTypes.Count == 0)
        {
            logger.LogInformation("No data migrations found.");
            return;
        }

        // Instantiate and sort by version
        var migrations = migrationTypes
            .Select(t => (IDataMigration)Activator.CreateInstance(t)!)
            .OrderBy(m => ParseVersion(m.Version))
            .ToList();

        // Get current stored platform version
        using var lookupScope = rootProvider.CreateScope();
        var lookupDb = lookupScope.ServiceProvider.GetRequiredService<DataContext>();
        var storedRecord = await lookupDb.systemVersions
            .FirstOrDefaultAsync(sv => sv.key == "platform");
        var storedVersion = storedRecord != null ? ParseVersion(storedRecord.version) : new Version(0, 0, 0);

        int applied = 0;

        foreach (var migration in migrations)
        {
            var migrationVersion = ParseVersion(migration.Version);
            if (migrationVersion <= storedVersion)
                continue;

            logger.LogInformation("Applying data migration {Version}: {Description}",
                migration.Version, migration.Description);

            // Each migration gets its own scope and transaction
            using var scope = rootProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();

            // Wrap in execution strategy to support MySqlRetryingExecutionStrategy
            var strategy = db.Database.CreateExecutionStrategy();
            try
            {
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync();

                    await migration.ExecuteAsync(scope.ServiceProvider, CancellationToken.None);

                    // Upsert SystemVersion inside the same transaction
                    var versionRecord = await db.systemVersions
                        .FirstOrDefaultAsync(sv => sv.key == "platform");

                    if (versionRecord != null)
                    {
                        versionRecord.version = migration.Version;
                        versionRecord.appliedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        db.systemVersions.Add(new SystemVersion
                        {
                            key = "platform",
                            version = migration.Version,
                            appliedAt = DateTime.UtcNow
                        });
                    }

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                applied++;
                logger.LogInformation("Data migration {Version} applied successfully.", migration.Version);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data migration {Version} failed. Stopping further migrations.", migration.Version);
                break; // Stop — subsequent migrations may depend on this one
            }
        }

        logger.LogInformation("Platform data migrations complete. Applied {Count} migration(s).", applied);
    }

    private static Version ParseVersion(string version)
    {
        return Version.TryParse(version, out var v) ? v : new Version(0, 0, 0);
    }
}
