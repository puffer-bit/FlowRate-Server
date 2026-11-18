using System.Buffers.Binary;
using Server.Application.Coordinator.Media;

namespace Server.Application.Tests.Coordinator.Media;

public class ImageDimensionsTests
{
    private static Task<(int Width, int Height)?> Read(byte[] content, string mimeType) =>
        ImageDimensions.ReadAsync(new MemoryStream(content), mimeType);

    [Fact]
    public async Task Png_SidesComeFromTheHeaderChunk()
    {
        var png = new byte[30];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 4032);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 3024);

        Assert.Equal((4032, 3024), await Read(png, "image/png"));
    }

    [Fact]
    public async Task Gif_SidesComeFromTheScreenDescriptor()
    {
        var gif = new byte[13];
        "GIF89a"u8.CopyTo(gif);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(6), 500);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(8), 280);

        Assert.Equal((500, 280), await Read(gif, "image/gif"));
    }

    [Fact]
    public async Task Jpeg_SidesComeFromTheFrameHeader_AfterTheMetadata()
    {
        var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);
        // An EXIF segment of 1000 bytes before the frame
        stream.Write([0xFF, 0xE1, 0x03, 0xEA]);
        stream.Write(new byte[1000]);
        // Fill bytes, then a progressive frame: precision, height, width
        stream.Write([0xFF, 0xFF, 0xC2, 0x00, 0x11, 0x08, 0x0B, 0xD0, 0x0F, 0xC0]);
        stream.Write(new byte[12]);

        Assert.Equal((4032, 3024), await Read(stream.ToArray(), "image/jpeg"));
    }

    [Fact]
    public async Task Jpeg_WithoutAFrameHeader_HasNoSides()
    {
        Assert.Null(await Read([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02], "image/jpeg"));
        Assert.Null(await Read([0xFF, 0xD8, 0xFF, 0xE1, 0x10, 0x00, 0x01], "image/jpeg"));
    }

    [Fact]
    public async Task WebP_Lossy_SidesComeFromTheKeyFrame()
    {
        var webp = WebP("VP8 ");
        new byte[] { 0, 0, 0, 0x9D, 0x01, 0x2A }.CopyTo(webp, 20);
        BinaryPrimitives.WriteUInt16LittleEndian(webp.AsSpan(26), 1920);
        BinaryPrimitives.WriteUInt16LittleEndian(webp.AsSpan(28), 1080);

        Assert.Equal((1920, 1080), await Read(webp, "image/webp"));
    }

    [Fact]
    public async Task WebP_Lossless_SidesAreStoredLessOne()
    {
        var webp = WebP("VP8L");
        webp[20] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(21), (uint)(799 | 599 << 14));

        Assert.Equal((800, 600), await Read(webp, "image/webp"));
    }

    [Fact]
    public async Task WebP_Extended_SidesComeFromTheCanvas()
    {
        var webp = WebP("VP8X");
        // 24-bit sides less one: 5000 and 3000
        new byte[] { 0x87, 0x13, 0x00, 0xB7, 0x0B, 0x00 }.CopyTo(webp, 24);

        Assert.Equal((5000, 3000), await Read(webp, "image/webp"));
    }

    [Fact]
    public async Task ContentOfAnotherType_HasNoSides()
    {
        Assert.Null(await Read(new byte[30], "image/png"));
        Assert.Null(await Read(new byte[30], "image/webp"));
        Assert.Null(await Read([1, 2], "image/gif"));
    }

    private static byte[] WebP(string chunk)
    {
        var webp = new byte[30];
        "RIFF"u8.CopyTo(webp);
        "WEBP"u8.CopyTo(webp.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(webp, 12);
        return webp;
    }
}
