using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Security;

namespace Delegat.Services.DataMigrations;

public class Migration_1_4_0_EncryptCredentials : IDataMigration
{
    public string Version => "1.4.0";
    public string Description => "Encrypt existing marketplace connection credentials";

    public async Task ExecuteAsync(IServiceProvider services, CancellationToken ct)
    {
        var encryption = services.GetService<EncryptionService>();
        if (encryption == null) return; // No key configured, skip

        var db = services.GetRequiredService<DataContext>();
        var connections = await db.Set<Models.Marketplace.MarketplaceConnection>().ToListAsync(ct);

        foreach (var conn in connections)
        {
            if (!string.IsNullOrEmpty(conn.credentialsJson) && !encryption.IsEncrypted(conn.credentialsJson))
                conn.credentialsJson = encryption.Encrypt(conn.credentialsJson);
        }

        await db.SaveChangesAsync(ct);
    }
}
