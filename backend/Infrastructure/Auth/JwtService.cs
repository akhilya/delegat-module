using Microsoft.IdentityModel.Tokens;
using PrintO.Infrastructure.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PrintO.Infrastructure.Auth;

/// <summary>
/// Interface for JWT token operations
/// </summary>
public interface IJwtService
{
    string GenerateToken(int userId, string userName, bool rememberMe = false);
    ClaimsPrincipal? ValidateToken(string token);
}

/// <summary>
/// JWT service for generating and validating tokens
/// Replaces Zorro.Modules.JwtBearer.JwtBearerService
/// </summary>
public class JwtService : IJwtService
{
    private readonly JwtSettings _settings;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;

    public JwtService(JwtSettings settings)
    {
        _settings = settings;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    }

    /// <summary>
    /// Generate a JWT token for a user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="userName">User name</param>
    /// <param name="rememberMe">If true, token expires in 30 days, otherwise 1 day</param>
    /// <returns>JWT token string</returns>
    public string GenerateToken(int userId, string userName, bool rememberMe = false)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, userName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.AuthTime, DateTime.UtcNow.ToString("O")),
            new Claim("userId", userId.ToString())
        };

        var expiry = rememberMe
            ? DateTime.UtcNow.AddDays(30)
            : DateTime.UtcNow.AddDays(1);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiry,
            signingCredentials: _signingCredentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Validate a JWT token
    /// </summary>
    /// <param name="token">Token string to validate</param>
    /// <returns>ClaimsPrincipal if valid, null otherwise</returns>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            return new JwtSecurityTokenHandler()
                .ValidateToken(token, _validationParameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
