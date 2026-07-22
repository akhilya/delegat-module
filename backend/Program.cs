using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Delegat;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Development;
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
using Delegat.Services.PlatformLink;
using Delegat.Marketplace.SDK;
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

// Encryption - required; fail fast if missing or invalid
var encryptionKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY");
if (string.IsNullOrWhiteSpace(encryptionKey))
    throw new InvalidOperationException(
        "ENCRYPTION_KEY environment variable is required (32-byte Base64-encoded AES-256 key).");
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

            var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var activeCacheKey = $"perm:user-active:{userId}";
            if (!cache.TryGetValue(activeCacheKey, out bool isActive))
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<DataContext>();
                isActive = await db.Users
                    .AsNoTracking()
                    .AnyAsync(u => u.Id == userId && u.isActive);
                if (isActive)
                    cache.Set(activeCacheKey, isActive, TimeSpan.FromMinutes(2));
            }

            if (!isActive)
            {
                context.Fail("User is inactive");
                return;
            }

            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                var permissionService = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
                // Remove any stale permission/role claims then hydrate from DB
                foreach (var claim in identity.FindAll(Permissions.ClaimType).ToList())
                    identity.RemoveClaim(claim);
                foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
                    identity.RemoveClaim(claim);
                foreach (var claim in identity.FindAll("is_admin").ToList())
                    identity.RemoveClaim(claim);
                await permissionService.HydrateClaimsAsync(identity, userId);
            }
        }
    };
})
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
    PublicApiAuthorizeAttribute.Scheme, _ => { });

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();
builder.Services.AddScoped<IPermissionService, PermissionService>();

// Infrastructure Services
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<Delegat.Controllers.PublicApi.PublicApiStoreContext>();
builder.Services.AddScoped<UserApiKeyService>();
builder.Services.AddSingleton<ApiRequestLogService>();
builder.Services.AddSingleton<IApiRequestLogger>(sp => sp.GetRequiredService<ApiRequestLogService>());
builder.Services.AddSingleton<IApiRequestLogQueryService>(sp => sp.GetRequiredService<ApiRequestLogService>());
builder.Services.AddHostedService<ApiRequestLogCleanupService>();
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
builder.Services.AddSingleton(mysqlSettings);
builder.Services.AddSingleton<IDatabaseDumpService, DatabaseDumpService>();
builder.Services.AddSingleton<ILoggingSettingsService, LoggingSettingsService>();

// Platform Link (Control Plane m2m) + Reference Catalog
var platformLinkOptions = PlatformLinkOptions.FromEnvironment();
builder.Services.AddSingleton(platformLinkOptions);
builder.Services.AddHttpClient<IPlatformLinkClient, PlatformLinkClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<PlatformLinkOptions>();
    if (!string.IsNullOrWhiteSpace(opts.ControlPlaneUrl))
        client.BaseAddress = new Uri(opts.ControlPlaneUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IReferenceCatalogService, ReferenceCatalogService>();
builder.Services.AddSingleton<IPlatformErrorReporter, PlatformErrorReporter>();
builder.Services.AddHostedService<ReferenceCatalogSyncHostedService>();
builder.Services.AddHostedService<PlatformHeartbeatHostedService>();

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
builder.Services.AddScoped<QuotaService>();
builder.Services.AddScoped<StoreStorageController>();

// Excel import/export
builder.Services.AddScoped<ExcelProductService>();

// Cross-store product clone/move
builder.Services.AddScoped<ProductTransferService>();

// Persisted error logs
builder.Services.AddScoped<IErrorLogService, ErrorLogService>();

// Outbound marketplace HTTP logs (singleton queue + query API)
builder.Services.AddSingleton<AdapterHttpLogService>();
builder.Services.AddSingleton<IAdapterHttpLogger>(sp => sp.GetRequiredService<AdapterHttpLogService>());
builder.Services.AddSingleton<IAdapterHttpLogQueryService>(sp => sp.GetRequiredService<AdapterHttpLogService>());
builder.Services.AddHostedService<AdapterHttpLogCleanupService>();

// AI services — registered only when AI_ENABLED is true at process start (restart to toggle).
var aiEnabled = FeatureFlags.IsAiEnabled();
if (aiEnabled)
{
    builder.Services.AddHttpClient<AiApiClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
    builder.Services.AddScoped<AiModelConfigService>();
    builder.Services.AddScoped<AiChatService>();
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
    builder.Services.AddScoped<MappingIntelligenceService>();
    builder.Services.AddScoped<AiOperationService>();
    builder.Services.AddScoped<GenerativeMappingService>();
}

// Sync services (MappingService resolves GenerativeMappingService optionally)
builder.Services.AddScoped<MappingService>(sp =>
{
    var generative = aiEnabled ? sp.GetService<GenerativeMappingService>() : null;
    return new MappingService(
        sp.GetRequiredService<DataContext>(),
        sp,
        generative);
});
builder.Services.AddScoped<CategoryImportService>();
builder.Services.AddScoped<ProductSyncService>();
builder.Services.AddScoped<GroupSyncService>();
builder.Services.AddScoped<PriceSyncService>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddScoped<PluginVersionSyncService>();

// Background services
builder.Services.AddHostedService<ImageDownloadBackgroundService>();
builder.Services.AddHostedService<ProductImportBackgroundService>();
builder.Services.AddHostedService<ProductExportBackgroundService>();
builder.Services.AddHostedService<ProductTransferBackgroundService>();
builder.Services.AddScoped<Delegat.Services.FolderJobs.FolderJobService>();
builder.Services.AddHostedService<FolderJobBackgroundService>();
builder.Services.AddHostedService<QueueCleanupBackgroundService>();

if (builder.Environment.IsDevelopment())
    builder.Services.AddHostedService<ViteDevServerHostedService>();

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
    options.SwaggerDoc("v1-public", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Delegat Public API",
        Version = "v1",
        Description =
            "Partner API for ERP/1C/n8n (API key auth).\n\n" +
            "**Auth:** Authorize with key `dk_…` only (Swagger adds Bearer).\n\n" +
            "**Docs:** see repository `docs/api/public-v1.md`.\n\n" +
            "**Async sync:** POST export/import endpoints return **202** + `operationId`; " +
            "poll `GET /api/v1/stores/{storeId}/operations/{operationId}`."
    });
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Delegat SPA API",
        Version = "v1"
    });
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        var path = apiDesc.RelativePath ?? "";
        var isPublic = path.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase);
        return docName == "v1-public" ? isPublic : !isPublic;
    });
    options.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "Paste the full API key only (starts with dk_). Do not type the word Bearer - Swagger adds it.",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "API Key"
    });
    options.OperationFilter<Delegat.Infrastructure.PublicApiSwaggerSecurityFilter>();

    var xmlPath = Path.Combine(AppContext.BaseDirectory, "Delegat.xml");
    if (System.IO.File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
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

    options.AddPolicy("public-api", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User?.FindFirst("apiKeyId")?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
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
        options.Conventions.Add(new Delegat.Infrastructure.AiControllersConvention(aiEnabled));
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

if (!aiEnabled)
{
    Console.WriteLine("AI_ENABLED=false: AI services and controllers are not registered (restart to enable).");
}

var app = builder.Build();

const string RolesAndPermissionsMigration = "20260721134512_RolesAndPermissions";
const string DropStoreAdministratorsMigration = "20260721134624_DropStoreAdministrators";

// Apply migrations automatically (with retry for slow MySQL startup).
// Two-phase: stop before DropStoreAdministrators so PermissionService can migrate legacy store-admins via EF.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<DataContext>>();
    var migrator = db.GetService<IMigrator>()
        ?? throw new InvalidOperationException("EF IMigrator is not available.");

    var pendingMigrations = db.Database.GetPendingMigrations().ToList();
    if (pendingMigrations.Any())
    {
        logger.LogInformation("Pending migrations to apply ({Count}): {Migrations}", pendingMigrations.Count, string.Join(", ", pendingMigrations));
    }

    var applied = db.Database.GetAppliedMigrations().ToHashSet();
    // Do not target an older migration if Drop already applied - that would roll back.
    string? phase1Target = applied.Contains(DropStoreAdministratorsMigration)
        ? null
        : RolesAndPermissionsMigration;
    await ApplyMigrationsWithRetryAsync(migrator, logger, phase1Target);
}

// Run platform data migrations
using (var scope = app.Services.CreateScope())
{
    await DataMigrationRunner.RunAsync(scope.ServiceProvider);
}

// Seed admin user if configured; migrate legacy access while StoreAdministrators still exists
var adminSettings = builder.Configuration.GetAdminSeedSettings();
using (var scope = app.Services.CreateScope())
{
    await DatabaseSeeder.SeedAsync(scope.ServiceProvider, adminSettings);
    var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
    await permissionService.MigrateLegacyAccessAsync();
}

// Apply remaining migrations (DropStoreAdministrators)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<DataContext>>();
    var migrator = db.GetService<IMigrator>()
        ?? throw new InvalidOperationException("EF IMigrator is not available.");
    await ApplyMigrationsWithRetryAsync(migrator, logger, targetMigration: null);
}

// Warm logging toggles cache (seed from env on first boot)
await app.Services.GetRequiredService<ILoggingSettingsService>().EnsureInitializedAsync();

// --reset-admin mode: generate a new admin password, print JSON to stdout, exit.
// Usage inside container: dotnet Delegat.dll --reset-admin
// Does not require restarting the running API process (separate process, same DB).
if (args.Contains("--reset-admin"))
{
    try
    {
        using var resetScope = app.Services.CreateScope();
        var result = await DatabaseSeeder.ResetAdminWithGeneratedPasswordAsync(
            resetScope.ServiceProvider,
            adminSettings.Email,
            adminSettings.Username);
        var permissionService = resetScope.ServiceProvider.GetRequiredService<IPermissionService>();
        var userManager = resetScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var adminUser = await userManager.FindByNameAsync(result.username)
            ?? await userManager.FindByEmailAsync(result.email);
        if (adminUser != null)
            await permissionService.EnsureOwnerAccessAsync(adminUser);
        Console.WriteLine(result.ToJson());
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
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

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1-public/swagger.json", "Public API v1");
    // SPA/internal API docs stay off production - partners only need Public API v1.
    if (!app.Environment.IsProduction())
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SPA API");
    c.DocumentTitle = "Delegat API";
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseAuthentication();
app.UseMiddleware<PublicApiRequestLoggingMiddleware>();
app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseRateLimiter();
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

static async Task ApplyMigrationsWithRetryAsync(
    IMigrator migrator,
    ILogger logger,
    string? targetMigration)
{
    const int maxRetries = 30;
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            var label = targetMigration ?? "latest";
            logger.LogInformation(
                "Applying database migrations to {Target} (attempt {Attempt}/{Max})...",
                label, i + 1, maxRetries);
            await migrator.MigrateAsync(targetMigration);
            logger.LogInformation("Database migrations applied successfully (target={Target}).", label);
            return;
        }
        catch (Exception ex)
        {
            string message = ex.InnerException?.Message ?? ex.Message;

            bool isSchemaDrift = message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Duplicate column", StringComparison.OrdinalIgnoreCase)
                || message.Contains("1050") || message.Contains("1060");

            if (isSchemaDrift)
            {
                logger.LogError(
                    "Schema drift detected while applying migrations. " +
                    "An object (table/column/index) already exists but the migration that should have created it " +
                    "is not recorded in __EFMigrationsHistory. Message: {Message}", message);
            }
            else
            {
                logger.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Message}", i + 1, maxRetries, message);
            }

            if (i == maxRetries - 1)
            {
                throw new InvalidOperationException(
                    "Failed to apply EF Core database migrations after all retries. " +
                    "If the logs mention 'already exists', __EFMigrationsHistory may be out of sync. " +
                    $"Last error: {message}", ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    throw new InvalidOperationException("Database migrations could not be applied.");
}

