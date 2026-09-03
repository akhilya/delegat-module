using System.Text.Json;
using Delegat.Infrastructure.Configuration;
using Microsoft.Playwright;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Chromium launch/emulation for public storefront pages (Ozon / Yandex).
/// Default headless Playwright 1.57+ uses chrome-headless-shell; channel "chromium"
/// is the full browser (new headless) and matches the advertised Chrome major.
/// </summary>
internal static class StorefrontPlaywrightProfile
{
    public const int ViewportWidth = 1920;
    public const int ViewportHeight = 1080;
    public const string DefaultChannel = "chromium";
    public const string Locale = "ru-RU";
    public const string TimezoneId = "Europe/Moscow";

    public static readonly string[] IgnoreDefaultArgs = ["--enable-automation"];

    public static readonly string[] LaunchArgs =
    [
        "--disable-blink-features=AutomationControlled",
        "--disable-dev-shm-usage",
        "--disable-infobars",
        "--no-first-run",
        "--no-default-browser-check",
        "--lang=ru-RU",
        "--window-size=1920,1080"
    ];

    public static string WindowsChromeUserAgent(string? browserVersion)
    {
        var major = ParseMajor(browserVersion) ?? 149;
        return $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36";
    }

    public static int? ParseMajor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        var token = version.Trim();
        var slash = token.LastIndexOf('/');
        if (slash >= 0 && slash < token.Length - 1)
            token = token[(slash + 1)..];
        var first = token.Split('.', 2)[0];
        return int.TryParse(first, out var n) && n is > 80 and < 400 ? n : null;
    }

    public static IReadOnlyDictionary<string, string> ExtraHeaders(string userAgent)
    {
        var major = (ParseMajor(userAgent) ?? 149).ToString();
        return new Dictionary<string, string>
        {
            ["Accept-Language"] = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7",
            ["Sec-CH-UA"] = $"\"Chromium\";v=\"{major}\", \"Google Chrome\";v=\"{major}\", \"Not.A/Brand\";v=\"99\"",
            ["Sec-CH-UA-Mobile"] = "?0",
            ["Sec-CH-UA-Platform"] = "\"Windows\"",
            ["Upgrade-Insecure-Requests"] = "1"
        };
    }

    public static string InitScript(string userAgent)
    {
        var uaJson = JsonSerializer.Serialize(userAgent);
        return $$"""
(() => {
  const ua = {{uaJson}};
  const hide = (obj, key, getter) => {
    try { Object.defineProperty(obj, key, { get: getter, configurable: true }); } catch (_) {}
  };
  hide(Navigator.prototype, 'webdriver', () => undefined);
  hide(navigator, 'webdriver', () => undefined);
  hide(navigator, 'platform', () => 'Win32');
  hide(navigator, 'userAgent', () => ua);
  hide(navigator, 'appVersion', () => String(ua).replace(/^Mozilla\//, ''));
  hide(navigator, 'vendor', () => 'Google Inc.');
  hide(navigator, 'language', () => 'ru-RU');
  hide(navigator, 'languages', () => Object.freeze(['ru-RU', 'ru', 'en-US', 'en']));
  if (!window.chrome) {
    window.chrome = { runtime: {}, app: { isInstalled: false }, csi() { return {}; }, loadTimes() { return {}; } };
  }
  const data = navigator.userAgentData;
  if (data) {
    const brands = data.brands;
    const patched = {
      brands,
      mobile: false,
      platform: 'Windows',
      getHighEntropyValues: async (hints) => {
        const base = typeof data.getHighEntropyValues === 'function'
          ? await data.getHighEntropyValues(hints)
          : {};
        return Object.assign({}, base, {
          brands,
          mobile: false,
          platform: 'Windows',
          platformVersion: '15.0.0',
          architecture: 'x86',
          bitness: '64',
          model: '',
        });
      },
      toJSON() { return { brands, mobile: false, platform: 'Windows' }; }
    };
    hide(navigator, 'userAgentData', () => patched);
  }
})();
""";
    }

    public static bool UrlMatches(string url, IReadOnlyList<string> filters)
    {
        foreach (var filter in filters)
        {
            if (filter.Length > 0 && url.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool LooksLikeChallenge(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;
        var lower = html.ToLowerInvariant();
        return lower.Contains("captcha", StringComparison.Ordinal)
               || lower.Contains("smartcaptcha", StringComparison.Ordinal)
               || lower.Contains("showcaptcha", StringComparison.Ordinal)
               || lower.Contains("challenge-platform", StringComparison.Ordinal)
               || lower.Contains("access denied", StringComparison.Ordinal)
               || lower.Contains("antibot", StringComparison.Ordinal)
               || lower.Contains("доступ ограничен", StringComparison.Ordinal)
               || lower.Contains("are you a robot", StringComparison.Ordinal);
    }

    public static BrowserTypeLaunchOptions CreateLaunchOptions(
        CatalogScrapePlaywrightOptions options,
        PlaywrightProxySettings? proxy,
        bool includeChannel)
    {
        var launch = new BrowserTypeLaunchOptions
        {
            Headless = options.Headless,
            Args = LaunchArgs,
            IgnoreDefaultArgs = IgnoreDefaultArgs
        };
        if (includeChannel && !string.IsNullOrWhiteSpace(options.Channel))
            launch.Channel = options.Channel;
        if (proxy != null)
        {
            launch.Proxy = new Proxy
            {
                Server = proxy.Server,
                Username = proxy.Username,
                Password = proxy.Password
            };
        }

        return launch;
    }

    public static BrowserNewContextOptions CreateContextOptions(string userAgent)
    {
        return new BrowserNewContextOptions
        {
            Locale = Locale,
            TimezoneId = TimezoneId,
            UserAgent = userAgent,
            ColorScheme = ColorScheme.Light,
            DeviceScaleFactor = 1,
            HasTouch = false,
            IsMobile = false,
            ViewportSize = new ViewportSize { Width = ViewportWidth, Height = ViewportHeight },
            ScreenSize = new ScreenSize { Width = ViewportWidth, Height = ViewportHeight },
            ExtraHTTPHeaders = ExtraHeaders(userAgent),
            Geolocation = new Geolocation { Latitude = 55.7558f, Longitude = 37.6173f },
            Permissions = ["geolocation"]
        };
    }
}
