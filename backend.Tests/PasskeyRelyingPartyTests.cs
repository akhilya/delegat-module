using Delegat.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

public class PasskeyRelyingPartyTests
{
    [Fact]
    public void Domain_UsesCurrentHost_NotParentRegistrableDomain()
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = "https";
        request.Host = new HostString("acme.i.module.delegat.pro");

        Assert.Equal("acme.i.module.delegat.pro", PasskeyRelyingParty.Domain(request));
        Assert.Equal("https://acme.i.module.delegat.pro", PasskeyRelyingParty.Origin(request));
        Assert.NotEqual("delegat.pro", PasskeyRelyingParty.Domain(request));
        Assert.NotEqual("i.module.delegat.pro", PasskeyRelyingParty.Domain(request));
    }

    [Fact]
    public void Origin_IncludesPort_DomainDoesNot()
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = "http";
        request.Host = new HostString("localhost", 5173);

        Assert.Equal("localhost", PasskeyRelyingParty.Domain(request));
        Assert.Equal("http://localhost:5173", PasskeyRelyingParty.Origin(request));
    }

    [Fact]
    public void Origin_PrefersBrowserOriginHeaderOverForwardedHost()
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = "http";
        request.Host = new HostString("localhost", 5080);
        request.Headers.Origin = "http://localhost:5173";

        Assert.Equal("localhost", PasskeyRelyingParty.Domain(request));
        Assert.Equal("http://localhost:5173", PasskeyRelyingParty.Origin(request));
    }
}
