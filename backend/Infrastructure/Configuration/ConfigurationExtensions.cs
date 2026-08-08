namespace Delegat.Infrastructure.Configuration;

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
        var publicEndpointRaw = GetValue(config, "MINIO_PUBLIC_ENDPOINT", "");
        var publicSecureStr = GetValue(config, "MINIO_PUBLIC_SECURE", "");
        var (publicEndpoint, secureFromUrl) = NormalizeMinioEndpoint(publicEndpointRaw);

        bool? publicSecure = null;
        if (!string.IsNullOrEmpty(publicSecureStr))
            publicSecure = bool.Parse(publicSecureStr);
        else if (secureFromUrl.HasValue)
            publicSecure = secureFromUrl;

        return new MinIOSettings
        {
            Endpoint = GetValue(config, "MINIO_ENDPOINT"),
            AccessKey = GetValue(config, "MINIO_ACCESS_KEY"),
            SecretKey = GetValue(config, "MINIO_SECRET_KEY"),
            Bucket = GetValue(config, "MINIO_BUCKET"),
            Secure = bool.Parse(GetValue(config, "MINIO_SECURE", "false")),
            PublicEndpoint = string.IsNullOrEmpty(publicEndpoint) ? null : publicEndpoint,
            PublicSecure = publicSecure
        };
    }

    /// <summary>
    /// MinIO.NET <c>WithEndpoint</c> expects <c>host[:port]</c> only. Strip a pasted
    /// <c>https://…</c> URL and optionally infer SSL from the scheme.
    /// </summary>
    internal static (string Endpoint, bool? SecureFromScheme) NormalizeMinioEndpoint(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ("", null);

        var s = raw.Trim();
        bool? secure = null;

        if (s.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Host))
        {
            if (uri.Scheme == Uri.UriSchemeHttps)
                secure = true;
            else if (uri.Scheme == Uri.UriSchemeHttp)
                secure = false;

            s = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }
        else
        {
            // Strip accidental path/query if pasted as host/path
            var slash = s.IndexOf('/');
            if (slash >= 0)
                s = s[..slash];
            var q = s.IndexOf('?');
            if (q >= 0)
                s = s[..q];
            s = s.Trim().Trim('.');
        }

        return (s, secure);
    }

    /// <summary>
    /// Get JWT settings from environment variables or configuration
    /// </summary>
    public static JwtSettings GetJwtSettings(this IConfiguration config)
    {
        // Empty ValidIssuer/ValidAudience makes JwtBearer reject every cookie with
        // "The audience 'empty' is invalid" (lab stacks often omit JWT_ISSUER/AUDIENCE).
        const string fallback = "delegat";
        var issuer = GetValue(config, "JWT_ISSUER", fallback);
        var audience = GetValue(config, "JWT_AUDIENCE", fallback);
        return new JwtSettings
        {
            Key = GetValue(config, "JWT_KEY"),
            Issuer = string.IsNullOrWhiteSpace(issuer) ? fallback : issuer,
            Audience = string.IsNullOrWhiteSpace(audience) ? fallback : audience,
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
            Password = GetValue(config, "ADMIN_PASSWORD", ""),
            ForceReset = GetValue(config, "ADMIN_FORCE_RESET", "false").Equals("true", StringComparison.OrdinalIgnoreCase)
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
