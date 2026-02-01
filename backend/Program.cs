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
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Configuration from environment variables
var mysqlSettings = builder.Configuration.GetMySqlSettings();
var minioSettings = builder.Configuration.GetMinIOSettings();
var jwtSettings = builder.Configuration.GetJwtSettings();

// Database
var connectionString = $"Server={mysqlSettings.Server};Port={mysqlSettings.Port};Database={mysqlSettings.Database};User={mysqlSettings.User};Password={mysqlSettings.Password};AllowPublicKeyRetrieval=True;SslMode=None;";
builder.Services.AddDbContext<DataContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

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
builder.Services.AddSingleton<IStorageService>(sp =>
{
    return new MinIOStorageService(minioSettings);
});

// Settings as singletons for DI
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton(minioSettings);

// TODO: Integrations will be re-implemented later
// builder.Services.AddScoped<OzonIntegration>();
// builder.Services.AddHostedService<OzonTasksInspector>();
// builder.Services.AddScoped<WbIntegration>();
// builder.Services.AddHostedService<WbTasksInspector>();

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
    options.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024; // 2GB
});

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

var app = builder.Build();

// Apply migrations automatically
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<DataContext>>();

    logger.LogInformation("Applying database migrations...");
    await db.Database.MigrateAsync();
    logger.LogInformation("Database migrations applied successfully.");
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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync("http://0.0.0.0:5000");
