using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class ImageSourceUrlTests
{
    [Fact]
    public void Normalize_StripsQueryAndFragment()
    {
        var normalized = ImageSourceUrl.Normalize(
            "https://cdn.example.test/photos/1.jpg?token=abc#top");

        Assert.Equal("https://cdn.example.test/photos/1.jpg", normalized);
    }

    [Fact]
    public void Normalize_LowercasesHost()
    {
        var normalized = ImageSourceUrl.Normalize("https://CDN.Example.TEST/a.png");

        Assert.Equal("https://cdn.example.test/a.png", normalized);
    }

    [Fact]
    public void EqualsNormalized_TreatsSignedQueryAsSamePhoto()
    {
        Assert.True(ImageSourceUrl.EqualsNormalized(
            "https://cdn.example.test/vol/big/1.webp?expires=1",
            "https://cdn.example.test/vol/big/1.webp?expires=2"));
    }

    [Fact]
    public void EqualsNormalized_DifferentPathsAreNotEqual()
    {
        Assert.False(ImageSourceUrl.EqualsNormalized(
            "https://cdn.example.test/a.jpg",
            "https://cdn.example.test/b.jpg"));
    }

    [Fact]
    public void Normalize_Blank_ReturnsNull()
    {
        Assert.Null(ImageSourceUrl.Normalize("  "));
        Assert.Null(ImageSourceUrl.Normalize(null));
    }
}
