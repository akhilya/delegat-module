using Microsoft.IdentityModel.Tokens;
using Delegat.Infrastructure.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Delegat.Infrastructure.Auth;

/// <summary>
/// Interface for JWT token operations
/// </summary>
public interface IJwtService
{
    string GenerateToken(int userId, string userName);
    ClaimsPrincipal? ValidateToken(string token);
    (string token, DateTime expiresAt) GenerateRefreshToken(bool rememberMe = false);
    ClaimsPrincipal? ValidateExpiredToken(string token);
}

/// <summary>
/// JWT service for generating and validating tokens
/// </summary>
public class JwtService : IJwtService
{
    private readonly JwtSettings _settings;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;
    private readonly TokenValidationParameters _refreshValidationParameters;

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
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        _refreshValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateLifetime = false,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Generate a JWT access token (15 min expiry)
    /// </summary>
    public string GenerateToken(int userId, string userName)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, userName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.AuthTime, DateTime.UtcNow.ToString("O")),
            new Claim("userId", userId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: _signingCredentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Generate a cryptographically random refresh token
    /// </summary>
    public (string token, DateTime expiresAt) GenerateRefreshToken(bool rememberMe = false)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes);
        var expiresAt = rememberMe
            ? DateTime.UtcNow.AddDays(30)
            : DateTime.UtcNow.AddDays(7);
        return (token, expiresAt);
    }

    /// <summary>
    /// Validate a JWT token (checks lifetime)
    /// </summary>
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

    /// <summary>
    /// Validate a JWT token ignoring lifetime (for refresh flow)
    /// </summary>
    public ClaimsPrincipal? ValidateExpiredToken(string token)
    {
        try
        {
            return new JwtSecurityTokenHandler()
                .ValidateToken(token, _refreshValidationParameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
