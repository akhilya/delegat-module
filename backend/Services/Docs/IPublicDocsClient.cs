namespace Delegat.Services.Docs;

public interface IPublicDocsClient
{
    Task<DocsCatalog> GetCatalogAsync(string locale, CancellationToken ct = default);
    Task<DocsPage> GetPageAsync(string slug, string locale, CancellationToken ct = default);
}
