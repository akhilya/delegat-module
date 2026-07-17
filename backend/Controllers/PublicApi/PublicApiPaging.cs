namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Shared pagination clamp for Public API list endpoints.
/// </summary>
public static class PublicApiPaging
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int DefaultOperationsPageSize = 20;
    public const int MaxOperationsPageSize = 100;

    /// <summary>
    /// Normalizes 1-based page and pageSize (defaults when &lt; 1, caps at max).
    /// </summary>
    public static (int page, int pageSize) Normalize(
        int page,
        int pageSize,
        int defaultPageSize = DefaultPageSize,
        int maxPageSize = MaxPageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = defaultPageSize;
        if (pageSize > maxPageSize) pageSize = maxPageSize;
        return (page, pageSize);
    }
}
