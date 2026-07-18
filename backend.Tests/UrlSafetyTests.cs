using Delegat.Infrastructure.Security;

namespace Delegat.Tests;

public class UrlSafetyTests
{
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com/")]
    public void RejectsUnsafeUrls(string url)
    {
        Assert.False(UrlSafety.TryValidateHttpUrl(url, out _, out _));
    }

    [Fact]
    public void AllowsPublicHttps()
    {
        Assert.True(UrlSafety.TryValidateHttpUrl("https://example.com/img.png", out var uri, out _));
        Assert.Equal("example.com", uri.Host);
    }

    [Fact]
    public void AllowsPrivateHostsWhenOptedIn()
    {
        Assert.True(UrlSafety.TryValidateHttpUrl(
            "http://127.0.0.1:1234/v1", out _, out _, allowPrivateHosts: true));
    }

    [Fact]
    public void SanitizeFileName_StripsPathTraversal()
    {
        Assert.Equal("evil.dll", FileNameSafety.Sanitize("../../../evil.dll"));
        Assert.Equal("file", FileNameSafety.Sanitize(".."));
    }
}
