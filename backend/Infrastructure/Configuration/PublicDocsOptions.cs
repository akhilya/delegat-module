namespace Delegat.Infrastructure.Configuration;

public sealed class PublicDocsOptions
{
    public const string DefaultBaseUrl = "https://module.delegat.pro/docs";

    public string BaseUrl { get; init; } = DefaultBaseUrl;
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(15);

    public static PublicDocsOptions FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("DOCS_PUBLIC_URL");
        return new PublicDocsOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(raw)
                ? DefaultBaseUrl
                : raw.Trim().TrimEnd('/'),
        };
    }
}
