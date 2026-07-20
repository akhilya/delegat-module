using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Delegat.Infrastructure.Configuration;
using Delegat.Models;

namespace Delegat.Infrastructure.Data;

public static class DatabaseSeeder
{
    /// <summary>Default base currency for stores created without an explicit value.</summary>
    private const string DefaultStoreBaseCurrency = Store.DEFAULT_BASE_CURRENCY;

    public static async Task SeedAsync(IServiceProvider services, AdminSeedSettings settings)
    {
        var logger = services.GetRequiredService<ILogger<DataContext>>();
        await EnsureStoreBaseCurrenciesAsync(services, logger);

        // Skip if no admin email configured
        if (string.IsNullOrEmpty(settings.Email) ||
            string.IsNullOrEmpty(settings.Username) ||
            string.IsNullOrEmpty(settings.Password))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<User>>();

        // Find by email first, then fall back to username (old accounts may have no email)
        var existingAdmin = await userManager.FindByEmailAsync(settings.Email)
            ?? await userManager.FindByNameAsync(settings.Username);

        if (existingAdmin != null)
        {
            if (!settings.ForceReset)
            {
                logger.LogInformation("Admin user already exists: {Username}", existingAdmin.UserName);
                return;
            }

            // Sync email and username if different
            bool profileChanged = false;
            if (existingAdmin.Email != settings.Email)
            {
                existingAdmin.Email = settings.Email;
                existingAdmin.EmailConfirmed = true;
                profileChanged = true;
            }
            if (existingAdmin.UserName != settings.Username)
            {
                existingAdmin.UserName = settings.Username;
                existingAdmin.NormalizedUserName = settings.Username.ToUpperInvariant();
                profileChanged = true;
            }
            if (profileChanged)
                await userManager.UpdateAsync(existingAdmin);

            // Reset password — set ADMIN_FORCE_RESET=true to trigger this
            var token = await userManager.GeneratePasswordResetTokenAsync(existingAdmin);
            var resetResult = await userManager.ResetPasswordAsync(existingAdmin, token, settings.Password);
            if (resetResult.Succeeded)
                logger.LogInformation("Admin reset: username={Username}, email={Email} (ADMIN_FORCE_RESET=true — remove after use)", settings.Username, settings.Email);
            else
                logger.LogError("Failed to reset admin: {Errors}", string.Join(", ", resetResult.Errors.Select(e => e.Description)));
            return;
        }

        // Create admin user
        var admin = new User
        {
            UserName = settings.Username,
            Email = settings.Email,
            isAdmin = true,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, settings.Password);

        if (result.Succeeded)
            logger.LogInformation("Admin user created: {Email}", settings.Email);
        else
            logger.LogError("Failed to create admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    private static async Task EnsureStoreBaseCurrenciesAsync(IServiceProvider services, ILogger logger)
    {
        var context = services.GetRequiredService<DataContext>();
        var updated = await context.stores
            .Where(s => s.baseCurrency == null || s.baseCurrency == "")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.baseCurrency, DefaultStoreBaseCurrency));

        if (updated > 0)
            logger.LogInformation("Set default base currency for {Count} store(s)", updated);
    }
}
