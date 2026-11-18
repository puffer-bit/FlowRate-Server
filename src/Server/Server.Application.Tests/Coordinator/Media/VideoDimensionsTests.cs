using System.Buffers.Binary;
using System.Text;
using Server.Application.Coordinator.Media;

namespace Server.Application.Tests.Coordinator.Media;

/// <summary>
/// Builds the boxes of an MP4 as far as the index goes: a movie header and the tracks with their sizes.
/// </summary>
internal static class TestMp4
{
    public static byte[] Build(int width, int height, double seconds, bool indexFirst = true, int mediaLength = 1000,
        bool withVideo = true, bool version1 = false)
    {
        const uint timescale = 1000;
        var duration = (ulong)(seconds * timescale);

        var mvhd = version1 ? new byte[4 + 8 + 8 + 4 + 8 + 80] : new byte[4 + 4 + 4 + 4 + 4 + 80];
        mvhd[0] = (byte)(version1 ? 1 : 0);
        var at = version1 ? 20 : 12;
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(at), timescale);
        if (version1)
            BinaryPrimitives.WriteUInt64BigEndian(mvhd.AsSpan(at + 4), duration);
        else
            BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(at + 4), (uint)duration);

        var tkhd = new byte[version1 ? 96 : 84];
        tkhd[0] = (byte)(version1 ? 1 : 0);
        var sizeAt = version1 ? 88 : 76;
        BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(sizeAt), (uint)width << 16);
        BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(sizeAt + 4), (uint)height << 16);

        var hdlr = new byte[24];
        Encoding.ASCII.GetBytes(withVideo ? "vide" : "soun").CopyTo(hdlr, 8);

        var moov = Box("moov", Box("mvhd", mvhd), Box("trak", Box("tkhd", tkhd), Box("mdia", Box("hdlr", hdlr))));
        var ftyp = Box("ftyp", "isomiso2"u8.ToArray());
        var mdat = Box("mdat", new byte[mediaLength]);
        return indexFirst ? [..ftyp, ..moov, ..mdat] : [..ftyp, ..mdat, ..moov];
    }

    private static byte[] Box(string type, params byte[][] children)
    {
        var body = children.SelectMany(child => child).ToArray();
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }
}

public class VideoDimensionsTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Mp4_GivesItsPictureAndLength(bool indexFirst, bool version1)
    {
        var file = TestMp4.Build(1280, 720, 12.5, indexFirst, version1: version1);

        var result = await VideoDimensions.ReadAsync(new MemoryStream(file));

        Assert.Equal((1280, 720, TimeSpan.FromSeconds(12.5)), result);
    }

    [Fact]
    public async Task Mp4_WithoutAVideoTrack_IsNotAVideo() =>
        Assert.Null(await VideoDimensions.ReadAsync(new MemoryStream(TestMp4.Build(0, 0, 3, withVideo: false))));

    [Fact]
    public async Task OtherContent_IsNotAnMp4()
    {
        Assert.Null(await VideoDimensions.ReadAsync(new MemoryStream([0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0])));
        Assert.Null(await VideoDimensions.ReadAsync(new MemoryStream("not a video at all"u8.ToArray())));
        Assert.Null(await VideoDimensions.ReadAsync(new MemoryStream(TestMp4.Build(640, 480, 3)[..40])));
    }

    [Theory]
    [InlineData("video/mp4", "clip.bin", true)]
    [InlineData("application/octet-stream", "clip.MOV", true)]
    [InlineData("application/pdf", "report.pdf", false)]
    public void Video_IsKnownByTypeOrName(string mimeType, string fileName, bool isVideo) =>
        Assert.Equal(isVideo, VideoDimensions.IsVideo(mimeType, fileName));
}
