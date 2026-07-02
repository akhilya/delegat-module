namespace Delegat.Infrastructure.Middleware;

public class CsrfHeaderMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS"
    };

    public CsrfHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!SafeMethods.Contains(context.Request.Method)
            && !context.Request.Path.StartsWithSegments("/hubs")
            && !context.Request.Headers.ContainsKey("X-CSRF"))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"title":"Missing CSRF header","status":403}""");
            return;
        }

        await _next(context);
    }
}
