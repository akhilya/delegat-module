using System.Text;
using System.Text.Json;

namespace Delegat.Tests;

/// <summary>
/// The Grok watch prompt is rendered from docs/ai/adapter-api-watch.json.
/// Adding or removing a plugin folder without updating that catalog fails this test.
/// The owner gets the new prompt from docs/ai/adapter-api-watch.md.
/// </summary>
public class AdapterApiWatchPromptTests
{
    [Fact]
    public void Prompt_MatchesCatalog_AndEveryPluginIsListed()
    {
        var root = FindRepoRoot();
        var catalogPath = Path.Combine(root, "docs", "ai", "adapter-api-watch.json");
        var promptPath = Path.Combine(root, "docs", "ai", "adapter-api-watch.md");
        var catalog = JsonSerializer.Deserialize<WatchCatalog>(File.ReadAllText(catalogPath), JsonOptions)!;
        var rendered = AdapterApiWatchPrompt.Render(catalog.Adapters).Replace("\r\n", "\n");

        var onDisk = PluginKeys(root);
        foreach (var key in onDisk)
            Assert.Contains(catalog.Adapters, a => a.Key == key);

        var gitmodules = File.ReadAllText(Path.Combine(root, ".gitmodules"));
        foreach (var adapter in catalog.Adapters)
        {
            var folder = Path.Combine(root, "plugins", adapter.FolderName);
            var adapterJson = Path.Combine(folder, "adapter.json");
            var listed = gitmodules.Contains($"path = plugins/{adapter.FolderName}", StringComparison.Ordinal);
            Assert.True(File.Exists(adapterJson) || listed, adapter.Key);
            if (!File.Exists(adapterJson))
                continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(adapterJson));
            Assert.Equal(adapter.Key, doc.RootElement.GetProperty("key").GetString());
        }

        var prompt = File.ReadAllText(promptPath).Replace("\r\n", "\n");
        const string start = "<!-- prompt -->\n";
        const string end = "<!-- /prompt -->";
        var from = prompt.IndexOf(start, StringComparison.Ordinal);
        var to = prompt.IndexOf(end, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from);
        var body = prompt[(from + start.Length)..to];
        Assert.Equal(rendered, body);
    }

    private static List<string> PluginKeys(string root)
    {
        var keys = new List<string>();
        var plugins = Path.Combine(root, "plugins");
        if (!Directory.Exists(plugins))
            return keys;
        foreach (var dir in Directory.GetDirectories(plugins))
        {
            var adapterJson = Path.Combine(dir, "adapter.json");
            if (!File.Exists(adapterJson))
                continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(adapterJson));
            var key = doc.RootElement.GetProperty("key").GetString();
            if (!string.IsNullOrEmpty(key))
                keys.Add(key);
        }
        return keys;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "ai", "adapter-api-watch.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root with docs/ai/adapter-api-watch.json was not found.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal sealed class WatchCatalog
    {
        public List<WatchAdapter> Adapters { get; set; } = new();
    }

    internal sealed class WatchAdapter
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string FolderName { get; set; } = "";
        public string Repository { get; set; } = "";
        public List<string> News { get; set; } = new();
        public List<string> Docs { get; set; } = new();
        public string Scope { get; set; } = "";
    }
}

internal static class AdapterApiWatchPrompt
{
    public static string Render(IEnumerable<AdapterApiWatchPromptTests.WatchAdapter> adapters)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You watch the official API news and documentation for every Delegat marketplace adapter listed below. Report what the platform plans to change, what it marks legacy, and what it has already changed. Do not edit repositories. Do not fill a gap from memory when a page does not open.");
        sb.AppendLine();
        sb.AppendLine("Window: notes and documentation changes dated after the date the owner gives. If no date is given, use the last 14 days.");
        sb.AppendLine();
        sb.AppendLine("For each adapter, open every News URL and every Documentation URL. Read release notes, deprecation banners, shutdown dates, and the method pages for paths this adapter calls. Then open the adapter repository and search those paths. Mark each item \"we call this\" or \"we do not call this\".");
        sb.AppendLine();
        sb.AppendLine("Report an item when any of these is true:");
        sb.AppendLine("- The platform named a shutdown date, a replacement, or a legacy mark.");
        sb.AppendLine("- A field this adapter sends was moved, renamed, retyped, or made required.");
        sb.AppendLine("- A method this adapter calls will be removed, or the docs now describe a new error for it.");
        sb.AppendLine("- The docs say a value belongs in a different JSON field.");
        sb.AppendLine("- A planned change will replace a method this adapter calls.");
        sb.AppendLine();
        sb.AppendLine("Do not report a brand-new method this adapter does not call, unless the note says it replaces one it does call.");
        sb.AppendLine();
        sb.AppendLine("Write the digest in Russian. One section per adapter, in the order below. For each item give the date, one of Upcoming, Legacy, Changed, or Removed, a quote from the official page, the link, whether this adapter calls that path, and the file in the adapter repository if you found one. If an adapter has no matching notes in the window, write \"без изменений\". If a page did not open, say which URL failed.");
        sb.AppendLine();
        sb.AppendLine("Do not propose a patch. Do not rewrite this prompt. The list below is the whole catalog. If an adapter you were told about is missing here, stop and say this prompt is stale.");
        sb.AppendLine();
        foreach (var adapter in adapters)
        {
            sb.AppendLine($"### {adapter.Name} (`{adapter.Key}`)");
            sb.AppendLine($"Repository: {adapter.Repository}");
            sb.AppendLine("News:");
            foreach (var url in adapter.News)
                sb.AppendLine($"- {url}");
            sb.AppendLine("Documentation:");
            foreach (var url in adapter.Docs)
                sb.AppendLine($"- {url}");
            sb.AppendLine(adapter.Scope);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
