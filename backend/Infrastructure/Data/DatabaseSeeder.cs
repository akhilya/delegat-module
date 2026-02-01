using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PrintO.Infrastructure.Configuration;
using PrintO.Models;

namespace PrintO.Infrastructure.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services, AdminSeedSettings settings)
    {
        // Skip if no admin email configured
        if (string.IsNullOrEmpty(settings.Email) ||
            string.IsNullOrEmpty(settings.Username) ||
            string.IsNullOrEmpty(settings.Password))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<User>>();
        var logger = services.GetRequiredService<ILogger<DataContext>>();

        // Check if admin already exists
        var existingAdmin = await userManager.FindByEmailAsync(settings.Email);
        if (existingAdmin != null)
        {
            logger.LogInformation("Admin user already exists: {Email}", settings.Email);
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
            logger.LogInformation("Admin user created successfully: {Email}", settings.Email);
        }
        else
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            logger.LogError("Failed to create admin user: {Errors}", errors);
        }
    }
}
