using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Delegat.Infrastructure.Auth;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission, bool requireStoreMembership)
    {
        Permission = permission;
        RequireStoreMembership = requireStoreMembership;
    }

    public string Permission { get; }
    public bool RequireStoreMembership { get; }
}

/// <summary>
/// Builds policies named <c>perm:{name}</c> and <c>perm.store:{name}</c> on demand.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string Prefix = "perm:";
    public const string StorePrefix = "perm.store:";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public static string PolicyName(string permission) => Prefix + permission;
    public static string StorePolicyName(string permission) => StorePrefix + permission;

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(StorePrefix, StringComparison.Ordinal))
        {
            var permission = policyName[StorePrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(permission, requireStoreMembership: true))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(permission, requireStoreMembership: false))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
