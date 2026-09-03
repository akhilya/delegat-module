using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Maps unofficial storefront HTTP failures (429 and antibot 403) to SPA ApiException
/// so they are not logged as unhandled 500s.
/// </summary>
internal static class MarketplaceStorefrontError
{
    public const string RateLimitedMessage =
        "The marketplace storefront is rate-limiting requests. Wait a few seconds and try again.";

    public const string BlockedMessage =
        "The marketplace storefront blocked the request (antibot or captcha). Try again later.";

    public static bool IsRateLimited(Exception ex)
        => HasStatus(ex, 429, HttpStatusCode.TooManyRequests);

    public static bool IsBlocked(Exception ex)
        => HasStatus(ex, 403, HttpStatusCode.Forbidden);

    public static Exception Map(Exception ex)
    {
        if (ex is ApiException)
            return ex;
        if (IsRateLimited(ex))
            return ApiException.TooManyRequests(RateLimitedMessage);
        if (IsBlocked(ex))
            return ApiException.Forbidden(BlockedMessage, "storefront_blocked");
        return ex;
    }

    public static async Task<T> GuardAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            var mapped = Map(ex);
            if (ReferenceEquals(mapped, ex))
                throw;
            throw mapped;
        }
    }

    private static bool HasStatus(Exception ex, int code, HttpStatusCode httpCode)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is ApiException api && api.StatusCode == code)
                return true;
            if (e is HttpRequestException http && http.StatusCode == httpCode)
                return true;
            var status = e.GetType().GetProperty("StatusCode")?.GetValue(e);
            if (status is int n && n == code)
                return true;
            if (status is HttpStatusCode named && named == httpCode)
                return true;
        }

        return false;
    }
}
