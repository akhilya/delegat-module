using Delegat.Infrastructure.Auth;

namespace Delegat.Tests;

public class TotpUriTests
{
    [Fact]
    public void Build_UsesUsernameAndDelegatIssuer()
    {
        var uri = TotpUri.Build("shop-admin", "JBSWY3DPEHPK3PXP");

        Assert.StartsWith("otpauth://totp/", uri, StringComparison.Ordinal);
        Assert.Contains("Delegat", uri, StringComparison.Ordinal);
        Assert.Contains("shop-admin", uri, StringComparison.Ordinal);
        Assert.Contains("secret=JBSWY3DPEHPK3PXP", uri, StringComparison.Ordinal);
        Assert.Contains("digits=6", uri, StringComparison.Ordinal);
        Assert.Contains("period=30", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=Delegat", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EscapesUsernameAndCustomIssuer()
    {
        var uri = TotpUri.Build("user name", "JBSWY3DPEHPK3PXP", "demo.i.module.delegat.pro");

        Assert.Contains("user%20name", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=demo.i.module.delegat.pro", uri, StringComparison.Ordinal);
        Assert.Contains("demo.i.module.delegat.pro:", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StripsSpacesFromSecret()
    {
        var uri = TotpUri.Build("admin", "JB SW Y3 DP");

        Assert.Contains("secret=JBSWY3DP", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("secret=JB SW", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatSecret_GroupsInFours()
    {
        Assert.Equal("JBSW Y3DP EHPK 3PXP", TotpUri.FormatSecret("jbswy3dpehpk3pxp"));
    }
}
