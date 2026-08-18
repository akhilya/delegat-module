using Delegat.Infrastructure;

namespace Delegat.Tests;

public class StringClipTests
{
    [Fact]
    public void Clip_TrimsAndFits()
    {
        Assert.Equal("Shop", StringClip.Clip("  Shop  ", 40));
    }

    [Fact]
    public void Clip_CutsOverMax()
    {
        Assert.Equal("abc", StringClip.Clip("abcdef", 3));
    }

    [Fact]
    public void ClipRequired_UsesFallbackWhenBlank()
    {
        Assert.Equal("Category 1", StringClip.ClipRequired("   ", 100, "Category 1"));
    }

    [Fact]
    public void ClipOptional_NullWhenBlank()
    {
        Assert.Null(StringClip.ClipOptional("  ", 100));
    }
}
