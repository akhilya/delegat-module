namespace PrintO.Infrastructure.Configuration;

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
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public bool Secure { get; set; } = true;
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
}
