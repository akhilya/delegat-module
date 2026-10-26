using Delegat.Services;
using ImageMagick;

namespace Delegat.Tests;

public class ThumbnailServiceTests
{
    [Theory]
    [InlineData(128, 128, 64)]
    [InlineData(512, 400, 200)]
    public void CreateWebpThumbnail_PreservesAspectRatioWithoutUpscaling(
        int targetWidth, uint expectedWidth, uint expectedHeight)
    {
        var settings = new MagickReadSettings { Width = 400, Height = 200 };
        using var source = new MagickImage("xc:red", settings);
        source.Format = MagickFormat.Png;
        using var input = new MemoryStream();
        source.Write(input);
        input.Position = 0;

        using var output = ThumbnailService.CreateWebpThumbnail(input, targetWidth, CancellationToken.None);
        using var thumbnail = new MagickImage(output);

        Assert.Equal(MagickFormat.WebP, thumbnail.Format);
        Assert.Equal(expectedWidth, thumbnail.Width);
        Assert.Equal(expectedHeight, thumbnail.Height);
    }
}
