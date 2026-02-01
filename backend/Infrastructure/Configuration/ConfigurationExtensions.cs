namespace PrintO.Infrastructure.Configuration;

/// <summary>
/// Extension methods to read configuration from environment variables
/// Replaces Zorro GetSecretValueUtility
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Get MySQL settings from environment variables or configuration
    /// </summary>
    public static MySqlSettings GetMySqlSettings(this IConfiguration config)
    {
        return new MySqlSettings
        {
            Server = GetValue(config, "MYSQL_SERVER"),
            Port = uint.Parse(GetValue(config, "MYSQL_PORT", "3306")),
            Database = GetValue(config, "MYSQL_DATABASE"),
            User = GetValue(config, "MYSQL_USER"),
            Password = GetValue(config, "MYSQL_PASSWORD")
        };
    }

    /// <summary>
    /// Get MinIO settings from environment variables or configuration
    /// </summary>
    public static MinIOSettings GetMinIOSettings(this IConfiguration config)
    {
        return new MinIOSettings
        {
            Endpoint = GetValue(config, "MINIO_ENDPOINT"),
            AccessKey = GetValue(config, "MINIO_ACCESS_KEY"),
            SecretKey = GetValue(config, "MINIO_SECRET_KEY"),
            Bucket = GetValue(config, "MINIO_BUCKET"),
            Secure = bool.Parse(GetValue(config, "MINIO_SECURE", "true"))
        };
    }

    /// <summary>
    /// Get JWT settings from environment variables or configuration
    /// </summary>
    public static JwtSettings GetJwtSettings(this IConfiguration config)
    {
        return new JwtSettings
        {
            Key = GetValue(config, "JWT_KEY"),
            Issuer = GetValue(config, "JWT_ISSUER"),
            Audience = GetValue(config, "JWT_AUDIENCE")
        };
    }

    /// <summary>
    /// Get Ozon settings from environment variables or configuration
    /// </summary>
    public static OzonSettings GetOzonSettings(this IConfiguration config)
    {
        return new OzonSettings
        {
            ApiKey = GetValue(config, "OZON_API_KEY", ""),
            ClientId = GetValue(config, "OZON_CLIENT_ID", "")
        };
    }

    /// <summary>
    /// Get Wildberries settings from environment variables or configuration
    /// </summary>
    public static WildberriesSettings GetWildberriesSettings(this IConfiguration config)
    {
        return new WildberriesSettings
        {
            ApiKey = GetValue(config, "WB_API_KEY", "")
        };
    }

    /// <summary>
    /// Get Admin seed settings from environment variables or configuration
    /// </summary>
    public static AdminSeedSettings GetAdminSeedSettings(this IConfiguration config)
    {
        return new AdminSeedSettings
        {
            Email = GetValue(config, "ADMIN_EMAIL", ""),
            Username = GetValue(config, "ADMIN_USERNAME", ""),
            Password = GetValue(config, "ADMIN_PASSWORD", "")
        };
    }

    /// <summary>
    /// Get a configuration value from environment variable or configuration file
    /// Environment variables take precedence
    /// </summary>
    private static string GetValue(IConfiguration config, string key, string defaultValue = "")
    {
        // First try environment variable
        var envValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(envValue))
            return envValue;

        // Then try configuration
        var configValue = config[key];
        if (!string.IsNullOrEmpty(configValue))
            return configValue;

        return defaultValue;
    }
}
