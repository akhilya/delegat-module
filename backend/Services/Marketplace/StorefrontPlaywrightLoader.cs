using System.Diagnostics;
using Delegat.Infrastructure.Configuration;
using Delegat.Marketplace.SDK;
using Microsoft.Playwright;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Chromium fallback for public storefront pages (Ozon / Yandex catalog scrape).
/// Does not solve interactive captcha; returns the page so callers can still map storefront_blocked.
/// </summary>
public sealed class StorefrontPlaywrightLoader : IStorefrontPageLoader, IAsyncDisposable
{
    private readonly CatalogScrapePlaywrightOptions _options;
    private readonly ILogger<StorefrontPlaywrightLoader> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _pageGate;
    private readonly HashSet<string> _warmedHosts = new(StringComparer.OrdinalIgnoreCase);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _session;
    private string? _userAgent;
    private bool _unavailable;
    private bool _loggedUnavailable;
    private bool _triedInstall;
    private bool _droppedChannel;

    public StorefrontPlaywrightLoader(
        CatalogScrapePlaywrightOptions options,
        ILogger<StorefrontPlaywrightLoader> logger)
    {
        _options = options;
        _logger = logger;
        _pageGate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentPages));
    }

    public async Task<StorefrontPageLoad?> LoadAsync(
        string url,
        IReadOnlyList<string>? captureUrlContains = null,
        CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(url))
            return null;

        await EnsureBrowserAsync(ct);
        if (_session == null)
            return null;

        var outcome = await RunPageAsync(url, captureUrlContains, ct);
        if (outcome.RecreateSession)
            await RecreateSessionAsync(ct);

        if (!outcome.RetryAfterRecreate)
            return outcome.Load;

        await EnsureBrowserAsync(ct);
        if (_session == null)
            return outcome.Load;

        outcome = await RunPageAsync(url, captureUrlContains, ct);
        if (outcome.RecreateSession)
            await RecreateSessionAsync(ct);
        return outcome.Load;
    }

    private async Task<PageLoadOutcome> RunPageAsync(
        string url,
        IReadOnlyList<string>? captureUrlContains,
        CancellationToken ct)
    {
        await _pageGate.WaitAsync(ct);
        try
        {
            var load = await LoadPageAsync(url, captureUrlContains, ct);
            return new PageLoadOutcome(load.Load, load.RecreateSession, RetryAfterRecreate: false);
        }
        catch (Exception ex) when (IsTargetClosed(ex))
        {
            _logger.LogWarning(ex, "Catalog scrape Chromium session closed; relaunching for {Url}", url);
            return new PageLoadOutcome(null, RecreateSession: true, RetryAfterRecreate: true);
        }
        finally
        {
            _pageGate.Release();
        }
    }

    private async Task<(StorefrontPageLoad? Load, bool RecreateSession)> LoadPageAsync(
        string url,
        IReadOnlyList<string>? captureUrlContains,
        CancellationToken ct)
    {
        var timeout = _options.TimeoutMs;
        var filters = captureUrlContains is { Count: > 0 } ? captureUrlContains : [];
        var captures = new List<string>();
        var captureLock = new object();
        var clock = Stopwatch.StartNew();
        IPage? page = null;
        var dropSession = false;
        try
        {
            page = await _session!.NewPageAsync();
            int Left(int floor) => Math.Max(floor, timeout - (int)clock.ElapsedMilliseconds);

            await WarmHostAsync(page, url, Left(4_000), ct);

            page.Response += (_, response) =>
            {
                _ = CaptureJsonAsync(response, filters, captures, captureLock);
            };

            Task<IResponse>? waitJson = null;
            if (filters.Count > 0)
            {
                waitJson = page.WaitForResponseAsync(
                    response => response.Ok && StorefrontPlaywrightProfile.UrlMatches(response.Url, filters),
                    new PageWaitForResponseOptions { Timeout = Left(8_000) });
            }

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = Left(8_000)
            });

            try
            {
                await page.Mouse.MoveAsync(164, 96);
                await page.Mouse.WheelAsync(0, 280);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                // Page swapped during a challenge redirect.
            }

            if (waitJson != null)
            {
                try { await waitJson; }
                catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
                {
                    // Composer XHR can arrive after hydration; fall through to a short wait.
                }
            }

            if (CountCaptures(captures, captureLock) == 0)
                await page.WaitForTimeoutAsync(Math.Min(2_500, Left(800)));

            ct.ThrowIfCancellationRequested();
            var html = await page.ContentAsync();
            if (StorefrontPlaywrightProfile.LooksLikeChallenge(html)
                && CountCaptures(captures, captureLock) == 0)
            {
                var extra = Math.Min(12_000, Left(3_000));
                try
                {
                    await page.WaitForResponseAsync(
                        response => response.Ok && (filters.Count == 0
                            || StorefrontPlaywrightProfile.UrlMatches(response.Url, filters)),
                        new PageWaitForResponseOptions { Timeout = extra });
                }
                catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
                {
                    await page.WaitForTimeoutAsync(Math.Min(1_500, extra));
                }

                html = await page.ContentAsync();
            }

            string? json;
            lock (captureLock)
            {
                json = captures.Count == 0
                    ? null
                    : captures.OrderByDescending(s => s.Length).First();
            }

            if (StorefrontPlaywrightProfile.LooksLikeChallenge(html) && string.IsNullOrWhiteSpace(json))
                dropSession = true;

            _logger.LogInformation(
                "Catalog scrape Chromium loaded {Url} (json={HasJson}, htmlChars={HtmlLen}, challenge={Challenge})",
                url,
                json != null,
                html?.Length ?? 0,
                dropSession);

            return (new StorefrontPageLoad(html ?? "", json), dropSession);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsTargetClosed(ex))
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Catalog scrape Chromium failed for {Url}", url);
            return (null, dropSession);
        }
        finally
        {
            if (page != null)
            {
                try { await page.CloseAsync(); }
                catch { /* ignore */ }
            }
        }
    }

    private async Task WarmHostAsync(IPage page, string url, int timeoutMs, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return;

        lock (_warmedHosts)
        {
            if (!_warmedHosts.Add(uri.Host))
                return;
        }

        if (uri.AbsolutePath is "/" or "")
            return;

        var home = uri.GetLeftPart(UriPartial.Authority) + "/";
        try
        {
            ct.ThrowIfCancellationRequested();
            await page.GotoAsync(home, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = timeoutMs
            });
            await page.WaitForTimeoutAsync(Math.Min(700, Math.Max(200, timeoutMs / 8)));
            try { await page.Mouse.MoveAsync(240, 140); }
            catch (PlaywrightException) { /* ignore */ }
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            _logger.LogDebug(ex, "Catalog scrape Chromium warmup skipped for {Host}", uri.Host);
        }
    }

    private static int CountCaptures(List<string> captures, object gate)
    {
        lock (gate)
            return captures.Count;
    }

    private static async Task CaptureJsonAsync(
        IResponse response,
        IReadOnlyList<string> filters,
        List<string> bag,
        object gate)
    {
        try
        {
            if (filters.Count == 0 || response.Status is < 200 or >= 300)
                return;
            if (!StorefrontPlaywrightProfile.UrlMatches(response.Url, filters))
                return;

            var text = await response.TextAsync();
            if (string.IsNullOrWhiteSpace(text))
                return;
            var trimmed = text.TrimStart();
            if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
                return;
            lock (gate)
                bag.Add(text);
        }
        catch
        {
            // Body already consumed or not text.
        }
    }

    private async Task EnsureBrowserAsync(CancellationToken ct)
    {
        if (_unavailable || _session != null)
            return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_unavailable || _session != null)
                return;
            if (_browser != null)
            {
                await OpenSessionUnlockedAsync();
                return;
            }

            await LaunchUnlockedAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task RecreateSessionAsync(CancellationToken ct)
    {
        await _initLock.WaitAsync(ct);
        try
        {
            await DisposeSessionUnlockedAsync();
            if (_unavailable)
                return;

            try
            {
                if (_browser != null)
                {
                    await OpenSessionUnlockedAsync();
                    return;
                }
            }
            catch (Exception ex) when (IsTargetClosed(ex))
            {
                await DisposeBrowserUnlockedAsync();
            }

            await LaunchUnlockedAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task LaunchUnlockedAsync()
    {
        var proxy = CatalogScrapePlaywrightOptions.ParseProxy(_options.Proxy);
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            await LaunchBrowserUnlockedAsync(proxy, includeChannel: !_droppedChannel);
            await OpenSessionUnlockedAsync();
            _logger.LogInformation(
                "Catalog scrape Chromium started (headless={Headless}, channel={Channel}, ua={UserAgent})",
                _options.Headless,
                _droppedChannel ? "(bundled)" : _options.Channel ?? "(default)",
                _userAgent);
        }
        catch (Exception ex)
        {
            if (!_droppedChannel
                && !string.IsNullOrWhiteSpace(_options.Channel)
                && !IsMissingBrowserExecutable(ex))
            {
                _droppedChannel = true;
                _logger.LogWarning(
                    ex,
                    "Catalog scrape Chromium channel {Channel} failed; retrying bundled browser",
                    _options.Channel);
                try
                {
                    await DisposeBrowserUnlockedAsync();
                    _playwright ??= await Playwright.CreateAsync();
                    await LaunchBrowserUnlockedAsync(proxy, includeChannel: false);
                    await OpenSessionUnlockedAsync();
                    _logger.LogInformation(
                        "Catalog scrape Chromium started without channel (headless={Headless})",
                        _options.Headless);
                    return;
                }
                catch (Exception fallbackEx)
                {
                    ex = fallbackEx;
                }
            }

            if (_options.InstallIfMissing
                && !_triedInstall
                && IsMissingBrowserExecutable(ex)
                && !IsBrandedChannel(_options.Channel))
            {
                _triedInstall = true;
                if (TryInstallChromium())
                {
                    try
                    {
                        _playwright ??= await Playwright.CreateAsync();
                        await LaunchBrowserUnlockedAsync(proxy, includeChannel: !_droppedChannel);
                        await OpenSessionUnlockedAsync();
                        _logger.LogInformation(
                            "Catalog scrape Chromium started after install (headless={Headless}, channel={Channel})",
                            _options.Headless,
                            _droppedChannel ? "(bundled)" : _options.Channel ?? "(default)");
                        return;
                    }
                    catch (Exception retryEx)
                    {
                        ex = retryEx;
                    }
                }
            }

            _unavailable = true;
            if (!_loggedUnavailable)
            {
                _loggedUnavailable = true;
                _logger.LogWarning(
                    ex,
                    "Catalog scrape Chromium is unavailable. HTTP-only scrape will keep returning storefront_blocked. "
                    + "Install browsers with: pwsh backend/bin/Debug/net8.0/playwright.ps1 install chromium. "
                    + "Set CATALOG_SCRAPE_PLAYWRIGHT=false to silence this.");
            }

            await DisposeBrowserUnlockedAsync();
        }
    }

    private async Task LaunchBrowserUnlockedAsync(PlaywrightProxySettings? proxy, bool includeChannel)
    {
        var launch = StorefrontPlaywrightProfile.CreateLaunchOptions(_options, proxy, includeChannel);
        _browser = await _playwright!.Chromium.LaunchAsync(launch);
    }

    private async Task OpenSessionUnlockedAsync()
    {
        if (_browser == null)
            return;

        _userAgent = StorefrontPlaywrightProfile.WindowsChromeUserAgent(_browser.Version);
        _session = await _browser.NewContextAsync(
            StorefrontPlaywrightProfile.CreateContextOptions(_userAgent));
        await _session.AddInitScriptAsync(StorefrontPlaywrightProfile.InitScript(_userAgent));
        lock (_warmedHosts)
            _warmedHosts.Clear();
    }

    private async Task DisposeSessionUnlockedAsync()
    {
        if (_session == null)
            return;
        try { await _session.DisposeAsync(); }
        catch { /* ignore */ }
        _session = null;
        lock (_warmedHosts)
            _warmedHosts.Clear();
    }

    private async Task DisposeBrowserUnlockedAsync()
    {
        await DisposeSessionUnlockedAsync();
        if (_browser != null)
        {
            try { await _browser.DisposeAsync(); }
            catch { /* ignore */ }
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _userAgent = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            await DisposeBrowserUnlockedAsync();
        }
        finally
        {
            _initLock.Release();
        }

        _initLock.Dispose();
        _pageGate.Dispose();
    }

    internal static bool IsMissingBrowserExecutable(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message ?? "";
            if (message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
                || message.Contains("download new browsers", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool IsTargetClosed(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message ?? "";
            if (message.Contains("Target closed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("has been closed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Browser has been closed", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool IsBrandedChannel(string? channel)
        => channel is "chrome" or "msedge"
           or "chrome-beta" or "chrome-dev" or "chrome-canary"
           or "msedge-beta" or "msedge-dev" or "msedge-canary";

    private bool TryInstallChromium()
    {
        try
        {
            _logger.LogInformation("Installing Playwright Chromium for catalog scrape…");
            var code = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (code == 0)
                return true;

            _logger.LogWarning("Playwright Chromium install exited {Code}", code);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playwright Chromium install failed");
            return false;
        }
    }

    private readonly record struct PageLoadOutcome(
        StorefrontPageLoad? Load,
        bool RecreateSession,
        bool RetryAfterRecreate);
}
