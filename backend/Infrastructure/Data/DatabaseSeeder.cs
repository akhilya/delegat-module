using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Models;
using System.Security.Cryptography;
using System.Text.Json;

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

            // Reset password - set ADMIN_FORCE_RESET=true to trigger this
            var token = await userManager.GeneratePasswordResetTokenAsync(existingAdmin);
            var resetResult = await userManager.ResetPasswordAsync(existingAdmin, token, settings.Password);
            if (resetResult.Succeeded)
                logger.LogInformation("Admin reset: username={Username}, email={Email} (ADMIN_FORCE_RESET=true - remove after use)", settings.Username, settings.Email);
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
        {
            logger.LogInformation("Admin user created: {Email}", settings.Email);
            var permissionService = services.GetRequiredService<IPermissionService>();
            await permissionService.EnsureOwnerAccessAsync(admin);
        }
        else
            logger.LogError("Failed to create admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    /// <summary>
    /// Break-glass: generate a new password for the platform admin, persist it, return credentials.
    /// Does not log the plaintext password.
    /// </summary>
    public static async Task<AdminResetResult> ResetAdminWithGeneratedPasswordAsync(
        IServiceProvider services,
        string email,
        string username)
    {
        var logger = services.GetRequiredService<ILogger<DataContext>>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        var password = GeneratePassword();

        var existingAdmin = await userManager.Users.FirstOrDefaultAsync(u => u.isAdmin)
            ?? (!string.IsNullOrEmpty(email) ? await userManager.FindByEmailAsync(email) : null)
            ?? (!string.IsNullOrEmpty(username) ? await userManager.FindByNameAsync(username) : null);

        if (existingAdmin != null)
        {
            if (!string.IsNullOrEmpty(email) && existingAdmin.Email != email)
            {
                existingAdmin.Email = email;
                existingAdmin.EmailConfirmed = true;
            }
            if (!string.IsNullOrEmpty(username) && existingAdmin.UserName != username)
            {
                existingAdmin.UserName = username;
                existingAdmin.NormalizedUserName = username.ToUpperInvariant();
            }
            existingAdmin.isAdmin = true;
            existingAdmin.isActive = true;
            await userManager.UpdateAsync(existingAdmin);

            var token = await userManager.GeneratePasswordResetTokenAsync(existingAdmin);
            var resetResult = await userManager.ResetPasswordAsync(existingAdmin, token, password);
            if (!resetResult.Succeeded)
            {
                var errors = string.Join(", ", resetResult.Errors.Select(e => e.Description));
                logger.LogError("Break-glass admin reset failed: {Errors}", errors);
                throw new InvalidOperationException($"Failed to reset admin password: {errors}");
            }

            logger.LogInformation(
                "Break-glass admin password reset for username={Username}, email={Email}",
                existingAdmin.UserName,
                existingAdmin.Email);

            var permissionService = services.GetRequiredService<IPermissionService>();
            await permissionService.EnsureOwnerAccessAsync(existingAdmin);

            return new AdminResetResult(existingAdmin.Email ?? email, existingAdmin.UserName ?? username, password);
        }

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(username))
            throw new InvalidOperationException("ADMIN_EMAIL and ADMIN_USERNAME are required to create an admin user.");

        var admin = new User
        {
            UserName = username,
            Email = email,
            isAdmin = true,
            isActive = true,
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(admin, password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            logger.LogError("Break-glass admin create failed: {Errors}", errors);
            throw new InvalidOperationException($"Failed to create admin user: {errors}");
        }

        logger.LogInformation("Break-glass admin user created: username={Username}, email={Email}", username, email);
        var permissionServiceCreate = services.GetRequiredService<IPermissionService>();
        await permissionServiceCreate.EnsureOwnerAccessAsync(admin);
        return new AdminResetResult(email, username, password);
    }

    public static string GeneratePassword(int length = 24)
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%^&*";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];

        // Ensure Identity password policy: digit + lowercase
        chars[0] = 'a';
        chars[1] = 'A';
        chars[2] = '2';
        chars[3] = '!';
        return new string(chars);
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

public record AdminResetResult(string email, string username, string password)
{
    public string ToJson() => JsonSerializer.Serialize(new { email, username, password });
}
