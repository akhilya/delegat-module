using System.Security.Claims;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Infrastructure.Auth;

public static class CabinetViewOnlyGuard
{
    public const string ErrorCode = "cabinet_view_only";

    public static void ThrowIfRestricted(ClaimsPrincipal? user)
    {
        var permissions = user?.FindAll(Permissions.ClaimType).Select(c => c.Value);
        if (!Permissions.IsCabinetViewOnly(permissions))
            return;

        throw ApiException.Forbidden(
            "This account can browse the cabinet but cannot change data.",
            code: ErrorCode);
    }
}
