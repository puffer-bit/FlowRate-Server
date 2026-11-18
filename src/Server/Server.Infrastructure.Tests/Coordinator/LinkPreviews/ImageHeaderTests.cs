using Server.Infrastructure.Coordinator.LinkPreviews;
using Xunit;

namespace Server.Infrastructure.Tests.Coordinator.LinkPreviews;

public class ImageHeaderTests
{
    [Fact]
    public void Png()
    {
        byte[] data = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52,
            0, 0, 0x04, 0xB0, 0, 0, 0x02, 0x76, 8, 6, 0, 0, 0];

        Assert.True(ImageHeader.TryRead(data, out var mime, out var width, out var height));
        Assert.Equal(("image/png", 1200, 630), (mime, width, height));
    }

    [Fact]
    public void Gif()
    {
        byte[] data = [.."GIF89a"u8, 0x40, 0x01, 0xF0, 0x00, 0, 0, 0];

        Assert.True(ImageHeader.TryRead(data, out var mime, out var width, out var height));
        Assert.Equal(("image/gif", 320, 240), (mime, width, height));
    }

    [Fact]
    public void Jpeg_SkipsSegmentsBeforeTheFrame()
    {
        byte[] data = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x4A, 0x46, // APP0 of 4 bytes
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.True(ImageHeader.TryRead(data, out var mime, out var width, out var height));
        Assert.Equal(("image/jpeg", 640, 480), (mime, width, height));
    }

    [Fact]
    public void WebP_Extended()
    {
        byte[] data = [.."RIFF"u8, 0, 0, 0, 0, .."WEBP"u8, .."VP8X"u8, 10, 0, 0, 0, 0, 0, 0, 0,
            0xFF, 0x03, 0x00, 0xFF, 0x01, 0x00];

        Assert.True(ImageHeader.TryRead(data, out var mime, out var width, out var height));
        Assert.Equal(("image/webp", 1024, 512), (mime, width, height));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    [InlineData("<html>not an image</html>")]
    public void OtherFormats_AreRefused(string text) =>
        Assert.False(ImageHeader.TryRead(System.Text.Encoding.UTF8.GetBytes(text), out _, out _, out _));
}
