using Delegat.Marketplace.SDK;

namespace Delegat.Tests;

public class DescriptionFormattingTests
{
    [Fact]
    public void ToMarketplaceHtml_DoubleNewline_ProducesTwoBr()
    {
        var result = DescriptionFormatting.ToMarketplaceHtml("a\n\nb");

        Assert.Equal("a<br>\n<br>\nb", result);
        Assert.Equal(2, CountOccurrences(result!, "<br>"));
    }

    [Fact]
    public void ToMarketplaceHtml_AlreadyHtml_IsIdempotent()
    {
        const string html = "a<br>\nb";
        Assert.Equal(html, DescriptionFormatting.ToMarketplaceHtml(html));
        Assert.Equal("x<p>y</p>", DescriptionFormatting.ToMarketplaceHtml("x<p>y</p>"));
    }

    [Fact]
    public void ToMarketplaceHtml_EscapesAngleBrackets()
    {
        Assert.Equal("a &lt; b", DescriptionFormatting.ToMarketplaceHtml("a < b"));
    }

    [Fact]
    public void ToMarketplaceHtml_PreservesInternalBlankLines_OnlyTrimsNothingInternally()
    {
        var result = DescriptionFormatting.ToMarketplaceHtml("a\n\n\nb");
        Assert.Equal("a<br>\n<br>\n<br>\nb", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ToMarketplaceHtml_NullOrEmpty_ReturnsSame(string? input)
    {
        Assert.Equal(input, DescriptionFormatting.ToMarketplaceHtml(input));
    }

    [Fact]
    public void ToMarketplaceHtml_NormalizesCrlf()
    {
        Assert.Equal("a<br>\nb", DescriptionFormatting.ToMarketplaceHtml("a\r\nb"));
    }

    [Fact]
    public void FromMarketplaceHtml_BrToNewline()
    {
        Assert.Equal("a\n\nb", DescriptionFormatting.FromMarketplaceHtml("a<br><br>b"));
        Assert.Equal("a\nb", DescriptionFormatting.FromMarketplaceHtml("a<br/>b"));
        Assert.Equal("a\nb", DescriptionFormatting.FromMarketplaceHtml("a<br />b"));
    }

    [Fact]
    public void FromMarketplaceHtml_DecodesEntities()
    {
        Assert.Equal("a < b", DescriptionFormatting.FromMarketplaceHtml("a &lt; b"));
    }

    [Fact]
    public void RoundTrip_PlainText_Survives()
    {
        const string plain = "line1\n\nline2 < tag";
        var html = DescriptionFormatting.ToMarketplaceHtml(plain);
        var back = DescriptionFormatting.FromMarketplaceHtml(html);
        Assert.Equal(plain, back);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = 0; (i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0; i += needle.Length)
            count++;
        return count;
    }
}
