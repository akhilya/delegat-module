using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace Delegat.Tests.Infrastructure;

/// <summary>Opt-in integration tests use disposable schemas on an explicitly configured local server only.</summary>
public sealed class LocalMySqlFactAttribute : FactAttribute
{
    public LocalMySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DELEGAT_TEST_MYSQL")))
            Skip = "Set DELEGAT_TEST_MYSQL to a local MySQL connection with CREATE/DROP DATABASE privileges.";
    }
}

internal sealed class LocalMySqlDatabase : IAsyncDisposable
{
    private readonly string _adminConnection;
    public string Name { get; } = "delegat_it_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = null!;

    private LocalMySqlDatabase(string connection)
    {
        var builder = new MySqlConnectionStringBuilder(connection);
        if (builder.Server is not ("127.0.0.1" or "localhost" or "::1"))
            throw new InvalidOperationException("Integration tests may only use a loopback MySQL server.");
        builder.Database = "";
        _adminConnection = builder.ConnectionString;
        builder.Database = Name;
        ConnectionString = builder.ConnectionString;
    }

    public DataContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseMySql(ConnectionString, new MySqlServerVersion(new Version(8, 4, 0)), mysql =>
            {
                mysql.CommandTimeout(60);
                mysql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(100), null);
            });
        if (interceptors.Length > 0) options.AddInterceptors(interceptors);
        return new DataContext(options.Options);
    }

    public static async Task<LocalMySqlDatabase> CreateAsync(bool migrate = false, string? migrationTarget = null)
    {
        var database = new LocalMySqlDatabase(Environment.GetEnvironmentVariable("DELEGAT_TEST_MYSQL")
            ?? throw new InvalidOperationException("DELEGAT_TEST_MYSQL is required."));
        await using var admin = new MySqlConnection(database._adminConnection);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{database.Name}` CHARACTER SET utf8mb4";
        await command.ExecuteNonQueryAsync();
        try
        {
            await using var db = database.CreateContext();
            if (migrationTarget != null) await db.GetService<IMigrator>().MigrateAsync(migrationTarget);
            else if (migrate) await db.Database.MigrateAsync();
            else await db.Database.EnsureCreatedAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(Name, "^delegat_it_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Refusing to drop an unexpected database name.");
        await using var admin = new MySqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{Name}`";
        await command.ExecuteNonQueryAsync();
        await using var schemaConnection = new MySqlConnection(ConnectionString);
        await MySqlConnection.ClearPoolAsync(schemaConnection);
    }
}
