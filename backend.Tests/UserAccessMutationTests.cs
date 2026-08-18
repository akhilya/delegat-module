using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Tests;

public class UserAccessMutationTests
{
    private static User Owner(int id = 1) => new() { Id = id, isAdmin = true, UserName = "owner" };
    private static User Member(int id = 2) => new() { Id = id, isAdmin = false, UserName = "member" };

    [Fact]
    public void EnsureCanAssign_RejectsChangingOwnAccess()
    {
        var actor = Owner(1);
        var ex = Assert.Throws<ApiException>(() =>
            UserAccessMutation.EnsureCanAssign(actor, actor, [AppRoles.Viewer]));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(UserAccessMutation.CannotChangeOwnAccessCode, ex.Code);
    }

    [Fact]
    public void EnsureCanAssign_RejectsRemovingOwnerRoleFromInstanceOwner()
    {
        var actor = Owner(1);
        var target = Owner(2);

        var ex = Assert.Throws<ApiException>(() =>
            UserAccessMutation.EnsureCanAssign(actor, target, [AppRoles.Viewer]));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(UserAccessMutation.OwnerRoleRequiredCode, ex.Code);
    }

    [Fact]
    public void EnsureCanAssign_AllowsOwnerToChangeAnotherNonOwner()
    {
        var actor = Owner(1);
        var target = Member(2);

        UserAccessMutation.EnsureCanAssign(actor, target, [AppRoles.Viewer]);
    }

    [Fact]
    public void EnsureCanAssign_AllowsOwnerToGrantOwnerRoleToAnother()
    {
        UserAccessMutation.EnsureCanAssign(Owner(1), Member(2), [AppRoles.Owner]);
    }

    [Fact]
    public void EnsureCanAssign_ForbidsNonOwnerFromAssigningOwnerRole()
    {
        var actor = Member(1);
        actor.isAdmin = false;
        var target = Member(2);

        var ex = Assert.Throws<ApiException>(() =>
            UserAccessMutation.EnsureCanAssign(actor, target, [AppRoles.Owner]));

        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public void NormalizeRoleNames_KeepsOwnerOnInstanceOwner()
    {
        var roles = UserAccessMutation.NormalizeRoleNames(targetIsAdmin: true, [AppRoles.Viewer]);

        Assert.Contains(AppRoles.Owner, roles);
        Assert.Contains(AppRoles.Viewer, roles);
    }

    [Fact]
    public void NormalizeRoleNames_DoesNotAddOwnerWhenNotAdmin()
    {
        var roles = UserAccessMutation.NormalizeRoleNames(targetIsAdmin: false, [AppRoles.Viewer]);

        Assert.DoesNotContain(AppRoles.Owner, roles);
        Assert.Equal([AppRoles.Viewer], roles);
    }

    [Fact]
    public void OwnerRoleOutOfSync_WhenAdminMissingOwnerRole()
    {
        Assert.True(UserAccessMutation.OwnerRoleOutOfSync(true, [AppRoles.Viewer]));
        Assert.False(UserAccessMutation.OwnerRoleOutOfSync(true, [AppRoles.Owner]));
        Assert.False(UserAccessMutation.OwnerRoleOutOfSync(false, [AppRoles.Viewer]));
    }
}
