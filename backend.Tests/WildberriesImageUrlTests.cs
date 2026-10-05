using Delegat.Marketplace.Wildberries;

namespace Delegat.Tests;

public class WildberriesImageUrlTests
{
    [Fact]
    public void GetImageUrls_ReadsAListOfObjectsFromTheMappingCanvas()
    {
        var data = new Dictionary<string, object>
        {
            ["images"] = new List<object?>
            {
                "https://cdn.example/gallery.jpg",
                "https://cdn.example/extra.png",
            },
        };

        var urls = WildberriesPlugin.GetImageUrls(data);

        Assert.Equal(
            ["https://cdn.example/gallery.jpg", "https://cdn.example/extra.png"],
            urls);
    }

    [Fact]
    public void GetImageUrls_StillSplitsACommaSeparatedString()
    {
        var data = new Dictionary<string, object>
        {
            ["images"] = "https://cdn.example/a.jpg, https://cdn.example/b.jpg",
        };

        var urls = WildberriesPlugin.GetImageUrls(data);

        Assert.Equal(
            ["https://cdn.example/a.jpg", "https://cdn.example/b.jpg"],
            urls);
    }
}
