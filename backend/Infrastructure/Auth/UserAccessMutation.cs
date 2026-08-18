using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Infrastructure.Auth;

/// <summary>
/// Guards role/grant writes so an instance owner cannot lock themselves out
/// of Administration → Users (Owner role carries users.manage; roles.manage
/// alone does not open that page).
/// </summary>
public static class UserAccessMutation
{
    public const string CannotChangeOwnAccessCode = "cannot_change_own_access";
    public const string OwnerRoleRequiredCode = "owner_role_required";

    public static bool OwnerRoleOutOfSync(bool isAdmin, IEnumerable<string> roleNames) =>
        isAdmin && !roleNames.Contains(AppRoles.Owner, StringComparer.OrdinalIgnoreCase);

    public static List<string> NormalizeRoleNames(bool targetIsAdmin, IReadOnlyList<string>? roleNames)
    {
        var desired = (roleNames ?? [])
            .Where(r => AppRoles.All.Contains(r, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetIsAdmin && !desired.Contains(AppRoles.Owner, StringComparer.OrdinalIgnoreCase))
            desired.Add(AppRoles.Owner);

        return desired;
    }

    public static void EnsureCanAssign(User actor, User target, IReadOnlyList<string>? desiredRoles)
    {
        var roles = desiredRoles ?? [];
        var wantsOwner = roles.Contains(AppRoles.Owner, StringComparer.OrdinalIgnoreCase);

        if (!actor.isAdmin && wantsOwner)
            throw ApiException.Forbidden("Only the instance owner can assign the Owner role");

        if (actor.Id == target.Id)
            throw ApiException.BadRequest(
                "Cannot change your own roles or extra permissions",
                code: CannotChangeOwnAccessCode);

        if (target.isAdmin && !wantsOwner)
            throw ApiException.BadRequest(
                "Cannot remove the Owner role from an instance owner",
                code: OwnerRoleRequiredCode);
    }
}
