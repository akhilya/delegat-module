using Delegat.Infrastructure.Storage;

namespace Delegat.Tests;

public sealed class VideoSignatureTests
{
    [Fact]
    public void VideoImportRejectsHtmlPlaylistsAndImagesEvenWithVideoMime()
    {
        foreach (var payload in new[] { "<html>player</html>", "#EXTM3U\nstream.ts", "GIF89a", "" })
            Assert.False(MinIOStorageService.TryGetVideoContentType("video/mp4", System.Text.Encoding.ASCII.GetBytes(payload), out _));
    }

    [Fact]
    public void VideoImportAcceptsKnownContainersButRejectsContradictoryMime()
    {
        var mp4 = new byte[] { 0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109 };
        Assert.True(MinIOStorageService.TryGetVideoContentType("application/octet-stream", mp4, out var mime));
        Assert.Equal("video/mp4", mime);
        Assert.False(MinIOStorageService.TryGetVideoContentType("text/html", mp4, out _));
        Assert.True(MinIOStorageService.TryGetVideoContentType(null, "RIFF0000AVI "u8, out mime));
        Assert.Equal("video/x-msvideo", mime);
        Assert.True(MinIOStorageService.TryGetVideoContentType("video/webm", new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }, out mime));
        Assert.Equal("video/webm", mime);
    }
}
