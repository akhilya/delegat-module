using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PrintO;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Configuration;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Infrastructure.Storage;
using PrintO.Models;
using PrintO.Services;
using PrintO.Services.Marketplace;
using PrintO.Services.Plugins;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Suppress EF Core SQL command logging (noisy in console)
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

// Configure Kestrel for large file uploads
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10L * 1024 * 1024 * 1024; // 10GB
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
});

// Configuration from environment variables
var mysqlSettings = builder.Configuration.GetMySqlSettings();
var minioSettings = builder.Configuration.GetMinIOSettings();
var jwtSettings = builder.Configuration.GetJwtSettings();

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
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
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
        ClockSkew = TimeSpan.Zero
    };

    // Read token from cookie
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.TryGetValue("access_token", out var token))
            {
                context.Token = token;
            }
            return Task.CompletedTask;
        }
    };
});

// Infrastructure Services
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddHttpClient("MinIOStorage", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IStorageService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    return new MinIOStorageService(minioSettings, httpClientFactory);
});

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

// Sync services
builder.Services.AddScoped<MappingService>();
builder.Services.AddScoped<CategoryImportService>();
builder.Services.AddScoped<ProductSyncService>();
builder.Services.AddScoped<GroupSyncService>();
builder.Services.AddScoped<PriceSyncService>();

// Background services
builder.Services.AddHostedService<ImageDownloadBackgroundService>();
builder.Services.AddHostedService<ProductImportBackgroundService>();
builder.Services.AddHostedService<ProductExportBackgroundService>();

// Marketplace adapters are now loaded as DLL plugins from ./plugins/ directory
// See: PrintO.Marketplace.SDK for plugin development

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
        var origins = new List<string> { "https://dashboard.printo.studio" };
        var environment = builder.Environment.EnvironmentName;

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
        options.Conventions.Add(new PrintO.Infrastructure.ApiRoutePrefixConvention());
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

// Seed admin user if configured
var adminSettings = builder.Configuration.GetAdminSeedSettings();
using (var scope = app.Services.CreateScope())
{
    await DatabaseSeeder.SeedAsync(scope.ServiceProvider, adminSettings);
}

// Middleware pipeline
app.UseMiddleware<ExceptionMiddleware>();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.MapGet("/api/health", () => Results.Ok("healthy"));

await app.RunAsync("http://0.0.0.0:5000");
