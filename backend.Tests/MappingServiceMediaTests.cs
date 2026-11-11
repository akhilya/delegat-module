using Delegat.Models;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public sealed class MappingServiceMediaTests
{
    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("IMAGE/WEBP", true)]
    [InlineData("video/mp4", false)]
    [InlineData("application/pdf", false)]
    [InlineData("application/octet-stream", false)]
    public void ImageProjection_UsesOnlyImageMimeTypes(string contentType, bool expected)
    {
        var file = new Models.File { contentType = contentType };
        Assert.Equal(expected, MappingService.IsImageMediaFile(file));
    }

    [Fact]
    public void ImageProjection_RejectsMissingFile()
        => Assert.False(MappingService.IsImageMediaFile(null));
}
