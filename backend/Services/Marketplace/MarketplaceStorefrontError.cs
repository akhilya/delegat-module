using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Maps unofficial storefront HTTP failures (especially 429) to SPA ApiException
/// so they are not logged as unhandled 500s.
/// </summary>
internal static class MarketplaceStorefrontError
{
    public const string RateLimitedMessage =
        "The marketplace storefront is rate-limiting requests. Wait a few seconds and try again.";

    public static bool IsRateLimited(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is ApiException api && api.StatusCode == StatusCodes.Status429TooManyRequests)
                return true;
            if (e is HttpRequestException http && http.StatusCode == HttpStatusCode.TooManyRequests)
                return true;
            var status = e.GetType().GetProperty("StatusCode")?.GetValue(e);
            if (status is int code && code == 429)
                return true;
            if (status is HttpStatusCode httpCode && httpCode == HttpStatusCode.TooManyRequests)
                return true;
        }

        return false;
    }

    public static Exception Map(Exception ex)
    {
        if (ex is ApiException)
            return ex;
        if (IsRateLimited(ex))
            return ApiException.TooManyRequests(RateLimitedMessage);
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
}
