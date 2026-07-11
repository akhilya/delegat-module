using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Delegat.Infrastructure.Data;

/// <summary>
/// Factory for creating DataContext at design time (for EF migrations).
/// Uses explicit IPv4 address to avoid IPv6 connection issues.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DataContext>
{
    public DataContext CreateDbContext(string[] args)
    {
        // Read from environment variables, fallback to defaults for local development
        var server = Environment.GetEnvironmentVariable("MYSQL_SERVER") ?? "127.0.0.1";
        var port = Environment.GetEnvironmentVariable("MYSQL_PORT") ?? "3307";
        var database = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "delegat";
        var user = Environment.GetEnvironmentVariable("MYSQL_USER") ?? "delegat";
        var password = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? "your_secure_password_here";

        // Force IPv4 by using 127.0.0.1 instead of localhost
        if (server == "localhost" || server == "mysql")
        {
            server = "127.0.0.1";
        }

        var connectionString = $"Server={server};Port={port};Database={database};User={user};Password={password};AllowPublicKeyRetrieval=True;SslMode=None;";

        var optionsBuilder = new DbContextOptionsBuilder<DataContext>();
        // Use explicit MySQL 8.4 version to avoid auto-detection connection
        var serverVersion = new MySqlServerVersion(new Version(8, 4, 8));
        optionsBuilder.UseMySql(connectionString, serverVersion);

        return new DataContext(optionsBuilder.Options);
    }
}
