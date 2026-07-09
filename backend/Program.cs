using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Delegat;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Controllers;
using Delegat.Services;
using Delegat.Services.DataMigrations;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Tools;
using Delegat.Services.Ai.Tools.Handlers;
using Delegat.Services.Marketplace;
using Delegat.Services.Plugins;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Delegat.Infrastructure.Security;
using Delegat.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Suppress EF Core SQL command logging (noisy in console)
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

// Configure Kestrel for large file uploads
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null; // No limit (endpoint-level control via attributes)
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
});

// Configuration from environment variables
var mysqlSettings = builder.Configuration.GetMySqlSettings();
var minioSettings = builder.Configuration.GetMinIOSettings();
var jwtSettings = builder.Configuration.GetJwtSettings();

// Encryption
var encryptionKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "";
if (!string.IsNullOrEmpty(encryptionKey))
    builder.Services.AddSingleton(new EncryptionService(encryptionKey));

// Database
var connectionString = $"Server={mysqlSettings.Server};Port={mysqlSettings.Port};Database={mysqlSettings.Database};User={mysqlSettings.User};Password={mysqlSettings.Password};AllowPublicKeyRetrieval=True;SslMode=None;";
var serverVersion = new MySqlServerVersion(new Version(8, 4, 8));
builder.Services.AddDbContext<DataContext>(options =>
    options.UseMySql(connectionString, serverVersion, mysqlOptions =>
        mysqlOptions.EnableRetryOnFailure(
            maxRetryCount: 10,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)));

// Identity
builder.Services.AddIdentity<User, UserRole>(options =>
{
    // Password policy
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;

    // Lockout policy
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<DataContext>()
.AddDefaultTokenProviders();

// JWT Authentication
var keyBytes = Encoding.UTF8.GetBytes(jwtSettings.Key);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    // Read token from cookie (or query string for SignalR WebSocket)
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // SignalR sends token via query string for WebSocket connections
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            else if (context.Request.Cookies.TryGetValue("access_token", out var token))
            {
                context.Token = token;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var userIdValue = context.Principal?.FindFirst("userId")?.Value;
            if (!int.TryParse(userIdValue, out var userId))
            {
                context.Fail("Invalid user token");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<DataContext>();
            var isActive = await db.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == userId && u.isActive);

            if (!isActive)
                context.Fail("User is inactive");
        }
    };
});

// Infrastructure Services
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddHttpClient("MinIOStorage", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddSingleton<IStorageService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<MinIOStorageService>>();
    return new MinIOStorageService(minioSettings, httpClientFactory, logger);
});

builder.Services.AddSingleton<ThumbnailService>();

// Settings as singletons for DI
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton(minioSettings);

// Marketplace Integration Services - Plugin System

// Plugin Manager (manages loading/unloading of DLL plugins)
builder.Services.AddSingleton<PluginManager>();

// Plugin Adapter Registry (implements IMarketplaceAdapterRegistry using plugins)
builder.Services.AddSingleton<PluginAdapterRegistry>();
builder.Services.AddSingleton<IMarketplaceAdapterRegistry>(sp => sp.GetRequiredService<PluginAdapterRegistry>());

// Plugin HostedService (loads plugins at startup)
builder.Services.AddHostedService<PluginHostedService>();

// Controllers resolved via DI (for cross-controller reuse)
builder.Services.AddScoped<ProductFolderController>();

// Quotas
builder.Services.AddScoped<AppSettingsService>();
builder.Services.AddScoped<QuotaService>();
builder.Services.AddScoped<StoreStorageController>();

// Excel import/export
builder.Services.AddScoped<ExcelProductService>();

// Cross-store product clone/move
builder.Services.AddScoped<ProductTransferService>();

// Persisted error logs
builder.Services.AddScoped<IErrorLogService, ErrorLogService>();

// AI services
builder.Services.AddHttpClient<AiApiClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddScoped<AiModelConfigService>();
builder.Services.AddScoped<AiChatService>();
builder.Services.AddScoped<AiMappingService>();
// AI tool handlers
builder.Services.AddScoped<ProductToolHandlers>();
builder.Services.AddScoped<CategoryToolHandlers>();
builder.Services.AddScoped<MappingToolHandlers>();
builder.Services.AddScoped<GroupToolHandlers>();
builder.Services.AddScoped<SyncToolHandlers>();
builder.Services.AddScoped<FileToolHandlers>();
builder.Services.AddScoped<AnalyticsToolHandlers>();
builder.Services.AddScoped<RollbackToolHandlers>();
builder.Services.AddScoped<ToolDispatcher>();
builder.Services.AddScoped<GraphMutationService>();
builder.Services.AddScoped<AiOperationService>();

// Sync services
builder.Services.AddScoped<MappingService>();
builder.Services.AddScoped<CategoryImportService>();
builder.Services.AddScoped<ProductSyncService>();
builder.Services.AddScoped<GroupSyncService>();
builder.Services.AddScoped<PriceSyncService>();
builder.Services.AddScoped<PluginVersionSyncService>();

// One-time startup repair for files broken by old rename behavior
builder.Services.AddHostedService<FileRepairService>();

// Background services
builder.Services.AddHostedService<ImageDownloadBackgroundService>();
builder.Services.AddHostedService<ProductImportBackgroundService>();
builder.Services.AddHostedService<ProductExportBackgroundService>();
builder.Services.AddHostedService<ProductTransferBackgroundService>();

// SignalR for real-time operation logs
builder.Services.AddSignalR();
builder.Services.AddSingleton<IOperationLogger, OperationLogger>();

// Marketplace adapters are now loaded as DLL plugins from ./plugins/ directory
// See: Delegat.Marketplace.SDK for plugin development

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(t => t.FullName!.Replace('+', '.'));
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var environment = builder.Environment.EnvironmentName;

        var corsOriginsEnv = Environment.GetEnvironmentVariable("CORS_ORIGINS") ?? "";
        var origins = corsOriginsEnv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (!origins.Any())
            origins.Add("https://dashboard.your-domain.com");

        if (environment == "Development" || environment == "Staging")
        {
            origins.Add("http://localhost:5173");
            origins.Add("http://127.0.0.1:5173");
        }

        policy
            .WithOrigins(origins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();

        if (environment == "Development")
        {
            policy.SetIsOriginAllowed(_ => true);
        }
    });
});

// Health checks
builder.Services.AddHealthChecks()
    .AddMySql(connectionString, name: "mysql")
    .AddCheck<Delegat.Infrastructure.Health.MinIOHealthCheck>("minio");

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Form options for large file uploads
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024; // 10GB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

// Controllers
builder.Services.AddControllers(options =>
    {
        options.Conventions.Add(new Delegat.Infrastructure.ApiRoutePrefixConvention());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

var app = builder.Build();

// Apply migrations automatically (with retry for slow MySQL startup)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<DataContext>>();

    const int maxRetries = 30;
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            logger.LogInformation("Applying database migrations...");
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
            break;
        }
        catch (Exception ex) when (i < maxRetries - 1)
        {
            logger.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Message}", i + 1, maxRetries, ex.InnerException?.Message ?? ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

}

// Run platform data migrations
using (var scope = app.Services.CreateScope())
{
    await DataMigrationRunner.RunAsync(scope.ServiceProvider);
}

// Seed admin user if configured
var adminSettings = builder.Configuration.GetAdminSeedSettings();
using (var scope = app.Services.CreateScope())
{
    await DatabaseSeeder.SeedAsync(scope.ServiceProvider, adminSettings);
}

// --reset-admin mode: reset superadmin credentials and exit without starting the web server.
// Usage inside container: dotnet Delegat.dll --reset-admin
if (args.Contains("--reset-admin"))
{
    if (string.IsNullOrEmpty(adminSettings.Email) || string.IsNullOrEmpty(adminSettings.Password))
    {
        Console.Error.WriteLine("ADMIN_EMAIL and ADMIN_PASSWORD must be set.");
        return 1;
    }
    var resetSettings = new AdminSeedSettings
    {
        Email = adminSettings.Email,
        Username = adminSettings.Username,
        Password = adminSettings.Password,
        ForceReset = true,
    };
    using var resetScope = app.Services.CreateScope();
    await DatabaseSeeder.SeedAsync(resetScope.ServiceProvider, resetSettings);
    Console.WriteLine("Admin reset complete.");
    return 0;
}

// Reset stuck InProgress tasks to Pending on startup (prevents permanent hang after container restart)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
    var resetLogger = scope.ServiceProvider.GetRequiredService<ILogger<DataContext>>();

    var stuckImports = await db.pendingProductImports
        .Where(p => p.status == ProductImportStatus.InProgress)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductImportStatus.Pending));

    var stuckExports = await db.pendingProductExports
        .Where(p => p.status == ProductExportStatus.InProgress)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductExportStatus.Pending));

    var stuckImages = await db.pendingImageDownloads
        .Where(p => p.status == ImageDownloadStatus.InProgress)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ImageDownloadStatus.Pending));

    if (stuckImports + stuckExports + stuckImages > 0)
        resetLogger.LogWarning("Startup recovery: reset {Imports} imports, {Exports} exports, {Images} image downloads from InProgress to Pending",
            stuckImports, stuckExports, stuckImages);
}

// Middleware pipeline
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<OperationHub>("/hubs/operations");
app.MapHealthChecks("/api/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString().ToLower(),
            version = PlatformVersion.Current,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString().ToLower(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        });
    }
});

await app.RunAsync("http://0.0.0.0:5000");
return 0;
