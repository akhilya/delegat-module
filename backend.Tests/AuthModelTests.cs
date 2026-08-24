using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class AuthModelTests
{
    [Fact]
    public void DataContext_MapsIdentityUserTokens_ForAuthenticatorKeys()
    {
        using var db = TestDbFactory.CreateDb();
        var entity = db.Model.FindEntityType(typeof(IdentityUserToken<int>));

        Assert.NotNull(entity);
        Assert.Equal("UserTokens", entity!.GetTableName());
    }

    [Fact]
    public void DataContext_MapsWebAuthnCredentials()
    {
        using var db = TestDbFactory.CreateDb();
        var entity = db.Model.FindEntityType(typeof(WebAuthnCredential));

        Assert.NotNull(entity);
        Assert.Equal("webAuthnCredentials", entity!.GetTableName());
    }
}
