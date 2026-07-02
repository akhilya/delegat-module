namespace Delegat.Services.DataMigrations;

/// <summary>
/// Interface for one-time data migrations that run when the platform version advances.
/// Each migration runs inside a transaction managed by DataMigrationRunner.
/// </summary>
public interface IDataMigration
{
    /// <summary>
    /// Semantic version at which this migration should run (e.g. "1.0.0").
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Human-readable description of what this migration does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Execute the data migration. The runner wraps this in a transaction externally.
    /// Resolve any needed services (DataContext, IUnitOfWork, etc.) from the provided IServiceProvider.
    /// </summary>
    Task ExecuteAsync(IServiceProvider services, CancellationToken ct);
}
