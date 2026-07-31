namespace Delegat.Infrastructure.Configuration;

/// <summary>
/// MySQL database connection settings
/// </summary>
public class MySqlSettings
{
    public string Server { get; set; } = string.Empty;
    public uint Port { get; set; } = 3306;
    public string Database { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// MinIO object storage settings
/// </summary>
public class MinIOSettings
{
    /// <summary>
    /// Internal endpoint for API calls (e.g., localhost:9002)
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Public endpoint for external URL generation (e.g., files.example.com).
    /// Used when generating URLs that external services (like Ozon) need to access.
    /// If not set, falls back to Endpoint.
    /// </summary>
    public string? PublicEndpoint { get; set; }

    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public bool Secure { get; set; } = false;

    /// <summary>
    /// Whether to use HTTPS for public URLs (separate from Secure which is for API calls).
    /// If not set, uses Secure value.
    /// </summary>
    public bool? PublicSecure { get; set; }
}

/// <summary>
/// JWT authentication settings
/// </summary>
public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}

/// <summary>
/// Ozon marketplace integration settings
/// </summary>
public class OzonSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}

/// <summary>
/// Wildberries marketplace integration settings
/// </summary>
public class WildberriesSettings
{
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Admin user seed settings (optional)
/// If all three are provided, an admin user will be created on startup
/// </summary>
public class AdminSeedSettings
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool ForceReset { get; set; } = false;
}
