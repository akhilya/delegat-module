using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;

namespace Delegat.Services.DataMigrations;

public class Migration_1_5_0_EncryptAiModelApiKeys : IDataMigration
{
    public string Version => "1.5.0";
    public string Description => "Encrypt plaintext AI model API keys";

    public async Task ExecuteAsync(IServiceProvider services, CancellationToken ct)
    {
        var encryption = services.GetService<EncryptionService>();
        if (encryption == null) return;

        var db = services.GetRequiredService<DataContext>();
        var models = await db.Set<AiModelConfig>().ToListAsync(ct);

        foreach (var model in models)
        {
            if (string.IsNullOrEmpty(model.apiKeyEncrypted))
                continue;
            if (encryption.IsCiphertext(model.apiKeyEncrypted))
                continue;

            model.apiKeyEncrypted = encryption.Encrypt(model.apiKeyEncrypted);
        }

        await db.SaveChangesAsync(ct);
    }
}
