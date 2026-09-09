namespace Delegat.Services.Docs;

public sealed class DocsCatalog
{
    public string Locale { get; set; } = "ru";
    public List<DocsCatalogGroup> Groups { get; set; } = [];
}

public sealed class DocsCatalogGroup
{
    public string Text { get; set; } = "";
    public List<DocsCatalogItem> Items { get; set; } = [];
}

public sealed class DocsCatalogItem
{
    public string Text { get; set; } = "";
    public string Slug { get; set; } = "";
}

public sealed class DocsPage
{
    public string Slug { get; set; } = "";
    public string Locale { get; set; } = "ru";
    public string Title { get; set; } = "";
    public string Markdown { get; set; } = "";
}
