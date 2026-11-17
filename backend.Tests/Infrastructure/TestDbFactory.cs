using Delegat.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Delegat.Tests.Infrastructure;

public static class TestDbFactory
{
    public static DataContext CreateDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new DataContext(options);
    }

    public static UnitOfWork CreateUnitOfWork(DataContext db) => new(db);

    /// <summary>
    /// A real relational context over shared-cache in-memory SQLite. The returned
    /// keeper connection must stay open for the database to live; extra contexts
    /// over the same DSN see the same data (for fence/transaction tests).
    /// </summary>
    public static (DataContext Db, SqliteConnection Keeper) CreateSqliteDb(
        bool retryingStrategy = false,
        IInterceptor[]? interceptors = null)
    {
        var dsn = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keeper = new SqliteConnection(dsn);
        keeper.Open();
        var db = CreateSqliteContext(dsn, retryingStrategy, interceptors);
        db.Database.EnsureCreated();
        return (db, keeper);
    }

    /// <summary>Second context over the same shared SQLite DSN.</summary>
    public static DataContext CreateSqliteContext(
        string dsn, bool retryingStrategy = false, IInterceptor[]? interceptors = null)
    {
        var builder = new DbContextOptionsBuilder<DataContext>()
            .UseSqlite(dsn, o =>
            {
                if (retryingStrategy)
                    o.ExecutionStrategy(deps => new RecordingRetryingExecutionStrategy(deps));
            });
        if (interceptors is { Length: > 0 })
            builder.AddInterceptors(interceptors);
        return new DataContext(builder.Options);
    }

    public static string SqliteDsn(DataContext db) =>
        db.Database.GetDbConnection().ConnectionString;

    /// <summary>
    /// Relational retrying strategy stand-in for tests: would retry transient-looking
    /// failures if it were ever invoked. The generated-artifact service wraps work in
    /// its own single-attempt strategy, so this must record zero executions.
    /// </summary>
    public sealed class RecordingRetryingExecutionStrategy(
        Microsoft.EntityFrameworkCore.Storage.ExecutionStrategyDependencies dependencies)
        : Microsoft.EntityFrameworkCore.Storage.ExecutionStrategy(dependencies, 5, TimeSpan.Zero)
    {
        public int Executions;

        public override async Task<TResult> ExecuteAsync<TState, TResult>(
            TState state,
            Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
            Func<DbContext, TState, CancellationToken, Task<Microsoft.EntityFrameworkCore.Storage.ExecutionResult<TResult>>>? verifySucceeded,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Executions);
            return await base.ExecuteAsync(state, operation, verifySucceeded, cancellationToken);
        }

        protected override bool ShouldRetryOn(Exception exception) => true;
    }
}
