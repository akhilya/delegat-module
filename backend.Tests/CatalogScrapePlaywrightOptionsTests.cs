using Delegat.Infrastructure.Configuration;
using Delegat.Marketplace.SDK;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class CatalogScrapePlaywrightOptionsTests
{
    [Fact]
    public void From_DefaultsWhenUnset()
    {
        var options = CatalogScrapePlaywrightOptions.From(_ => null);

        Assert.True(options.Enabled);
        Assert.True(options.Headless);
        Assert.True(options.InstallIfMissing);
        Assert.Null(options.Proxy);
        Assert.Equal("chromium", options.Channel);
        Assert.Equal(60_000, options.TimeoutMs);
        Assert.Equal(1, options.MaxConcurrentPages);
    }

    [Fact]
    public void From_ParsesFalseAndProxy()
    {
        string? Env(string key) => key switch
        {
            "CATALOG_SCRAPE_PLAYWRIGHT" => "false",
            "CATALOG_SCRAPE_PLAYWRIGHT_HEADLESS" => "0",
            "CATALOG_SCRAPE_PLAYWRIGHT_PROXY" => "http://u:p@127.0.0.1:8080",
            "CATALOG_SCRAPE_PLAYWRIGHT_CHANNEL" => "chrome",
            "CATALOG_SCRAPE_PLAYWRIGHT_TIMEOUT_MS" => "12000",
            "CATALOG_SCRAPE_PLAYWRIGHT_CONCURRENCY" => "4",
            "CATALOG_SCRAPE_PLAYWRIGHT_INSTALL" => "false",
            _ => null
        };

        var options = CatalogScrapePlaywrightOptions.From(Env);

        Assert.False(options.Enabled);
        Assert.False(options.Headless);
        Assert.Equal("http://u:p@127.0.0.1:8080", options.Proxy);
        Assert.Equal("chrome", options.Channel);
        Assert.Equal(12_000, options.TimeoutMs);
        Assert.Equal(4, options.MaxConcurrentPages);
        Assert.False(options.InstallIfMissing);
    }

    [Fact]
    public void ParseProxy_ReadsUserInfo()
    {
        var proxy = CatalogScrapePlaywrightOptions.ParseProxy("http://user:s3cret@proxy.example:3128");

        Assert.NotNull(proxy);
        Assert.Equal("http://proxy.example:3128", proxy!.Server);
        Assert.Equal("user", proxy.Username);
        Assert.Equal("s3cret", proxy.Password);
    }

    [Fact]
    public void ParseProxy_NullWhenEmpty()
    {
        Assert.Null(CatalogScrapePlaywrightOptions.ParseProxy("  "));
        Assert.Null(CatalogScrapePlaywrightOptions.ParseProxy(null));
    }

    [Fact]
    public void ParseChannel_DefaultsChromiumAndAllowsNone()
    {
        Assert.Equal("chromium", CatalogScrapePlaywrightOptions.ParseChannel(null));
        Assert.Equal("chrome", CatalogScrapePlaywrightOptions.ParseChannel("Chrome"));
        Assert.Null(CatalogScrapePlaywrightOptions.ParseChannel("none"));
        Assert.Null(CatalogScrapePlaywrightOptions.ParseChannel("off"));
    }
}

public class StorefrontPlaywrightLoaderTests
{
    [Fact]
    public void IsMissingBrowserExecutable_DetectsPlaywrightInstallHint()
    {
        var ex = new InvalidOperationException(
            "Executable doesn't exist at C:\\ms-playwright\\chrome.exe");
        Assert.True(StorefrontPlaywrightLoader.IsMissingBrowserExecutable(ex));
        Assert.True(StorefrontPlaywrightLoader.IsMissingBrowserExecutable(
            new Exception("outer", new Exception("Please run the following command to download new browsers"))));
        Assert.False(StorefrontPlaywrightLoader.IsMissingBrowserExecutable(
            new InvalidOperationException("timeout")));
        Assert.True(StorefrontPlaywrightLoader.IsTargetClosed(
            new Exception("Target page, context or browser has been closed")));
        Assert.True(StorefrontPlaywrightLoader.IsBrandedChannel("chrome"));
        Assert.False(StorefrontPlaywrightLoader.IsBrandedChannel("chromium"));
    }
}

public class StorefrontPlaywrightProfileTests
{
    [Fact]
    public void WindowsChromeUserAgent_UsesBrowserMajorNotStale122()
    {
        var ua = StorefrontPlaywrightProfile.WindowsChromeUserAgent("149.0.7827.55");
        Assert.Contains("Chrome/149.0.0.0", ua, StringComparison.Ordinal);
        Assert.DoesNotContain("Chrome/122", ua, StringComparison.Ordinal);
        Assert.Contains("Windows NT 10.0", ua, StringComparison.Ordinal);
    }

    [Fact]
    public void InitScript_HidesWebdriverAndClaimsWindows()
    {
        var script = StorefrontPlaywrightProfile.InitScript(
            StorefrontPlaywrightProfile.WindowsChromeUserAgent("149.0.0.0"));
        Assert.Contains("webdriver", script, StringComparison.Ordinal);
        Assert.Contains("Win32", script, StringComparison.Ordinal);
        Assert.Contains("platform: 'Windows'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Chrome/122", script, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchOptions_UseChromiumChannelAndDropAutomationSwitch()
    {
        var options = CatalogScrapePlaywrightOptions.From(_ => null);
        var launch = StorefrontPlaywrightProfile.CreateLaunchOptions(options, proxy: null, includeChannel: true);

        Assert.Equal("chromium", launch.Channel);
        Assert.Contains("--enable-automation", launch.IgnoreDefaultArgs!);
        Assert.Contains("--disable-blink-features=AutomationControlled", launch.Args!);
        Assert.Null(StorefrontPlaywrightProfile.CreateLaunchOptions(options, null, includeChannel: false).Channel);
    }

    [Fact]
    public void ContextOptions_MatchDesktopChromeInMoscow()
    {
        var ua = StorefrontPlaywrightProfile.WindowsChromeUserAgent("149.0.0.0");
        var ctx = StorefrontPlaywrightProfile.CreateContextOptions(ua);

        Assert.Equal("ru-RU", ctx.Locale);
        Assert.Equal("Europe/Moscow", ctx.TimezoneId);
        Assert.Equal(ua, ctx.UserAgent);
        Assert.Equal(1920, ctx.ViewportSize!.Width);
        Assert.Equal(1080, ctx.ViewportSize.Height);
        Assert.Equal(1920, ctx.ScreenSize!.Width);
        Assert.Equal(1080, ctx.ScreenSize.Height);
        Assert.Contains("Sec-CH-UA-Platform", ctx.ExtraHTTPHeaders!.Select(h => h.Key));
    }

    [Fact]
    public void DesktopChromeUserAgent_IsCurrentMajor()
    {
        Assert.Contains("Chrome/149", StorefrontHttp.DesktopChromeUserAgent, StringComparison.Ordinal);
        Assert.DoesNotContain("Chrome/122", StorefrontHttp.DesktopChromeUserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public void UrlMatches_AndChallengeHints()
    {
        Assert.True(StorefrontPlaywrightProfile.UrlMatches(
            "https://www.ozon.ru/api/composer-api.bx/page/json/v2?url=/search",
            ["composer-api.bx"]));
        Assert.False(StorefrontPlaywrightProfile.UrlMatches(
            "https://www.ozon.ru/product/1",
            ["composer-api.bx"]));
        Assert.True(StorefrontPlaywrightProfile.LooksLikeChallenge("<html>smartcaptcha</html>"));
        Assert.False(StorefrontPlaywrightProfile.LooksLikeChallenge("<html>AirPods Pro</html>"));
    }
}
