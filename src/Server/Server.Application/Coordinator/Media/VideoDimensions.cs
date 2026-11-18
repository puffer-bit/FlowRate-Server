using System.Buffers.Binary;
using System.Text;

namespace Server.Application.Coordinator.Media;

/// <summary>
/// Reads the size of the picture and the length of an MP4 or QuickTime video from its index, the
/// "moov" box, without reading the media. The sides are those of the track as shown, before its rotation,
/// which does not change the shorter one.
/// </summary>
public static class VideoDimensions
{
    // An index larger than this is not read: a long video has a few megabytes of it
    private const long MaxIndexLength = 64L * 1024 * 1024;

    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".3gp", ".wmv", ".flv", ".ts", ".mts" };

    /// <summary>A file that plays as a video in the chat, by its type or its name.</summary>
    public static bool IsVideo(string mimeType, string fileName) =>
        mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
        VideoExtensions.Contains(Path.GetExtension(fileName));

    /// <returns>Null when the content is not an MP4 or QuickTime file with a video track.</returns>
    public static async Task<(int Width, int Height, TimeSpan Duration)?> ReadAsync(Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!content.CanSeek)
            return null;

        var header = new byte[16];
        long position = 0;
        var length = content.Length;
        while (position + 8 <= length)
        {
            content.Position = position;
            await content.ReadExactlyAsync(header.AsMemory(0, 8), cancellationToken);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header, 4, 4);
            var headerLength = 8;
            if (size == 1)
            {
                if (position + 16 > length)
                    return null;
                await content.ReadExactlyAsync(header.AsMemory(8, 8), cancellationToken);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                headerLength = 16;
            }
            else if (size == 0)
                size = length - position;
            if (size < headerLength || position + size > length)
                return null;

            if (type == "moov")
            {
                if (size - headerLength > MaxIndexLength)
                    return null;
                var moov = new byte[size - headerLength];
                await content.ReadExactlyAsync(moov, cancellationToken);
                return ReadIndex(moov);
            }
            // Only boxes of an ISO media file are walked through; anything else is not one
            if (position == 0 && type is not ("ftyp" or "wide" or "free" or "skip" or "mdat" or "moov"))
                return null;
            position += size;
        }
        return null;
    }

    private static (int, int, TimeSpan)? ReadIndex(ReadOnlySpan<byte> moov)
    {
        TimeSpan? duration = null;
        (int Width, int Height)? picture = null;
        foreach (var (type, body) in Boxes(moov))
        {
            switch (type)
            {
                case "mvhd":
                    duration = ReadMovieDuration(body);
                    break;
                case "trak" when picture == null:
                    picture = ReadVideoTrack(body);
                    break;
            }
        }
        return duration is { } length && picture is var (width, height) ? (width, height, length) : null;
    }

    private static TimeSpan? ReadMovieDuration(ReadOnlySpan<byte> mvhd)
    {
        if (mvhd.Length < 1)
            return null;
        // Version 1 has 64-bit times and duration
        var (timescaleAt, durationLength) = mvhd[0] == 1 ? (20, 8) : (12, 4);
        if (mvhd.Length < timescaleAt + 4 + durationLength)
            return null;
        var timescale = BinaryPrimitives.ReadUInt32BigEndian(mvhd[timescaleAt..]);
        var units = durationLength == 8
            ? BinaryPrimitives.ReadUInt64BigEndian(mvhd[(timescaleAt + 4)..])
            : BinaryPrimitives.ReadUInt32BigEndian(mvhd[(timescaleAt + 4)..]);
        return timescale == 0 ? null : TimeSpan.FromSeconds((double)units / timescale);
    }

    private static (int, int)? ReadVideoTrack(ReadOnlySpan<byte> trak)
    {
        (int, int)? size = null;
        var isVideo = false;
        foreach (var (type, body) in Boxes(trak))
        {
            if (type == "tkhd")
                size = ReadTrackSize(body);
            else if (type == "mdia")
                foreach (var (mediaType, mediaBody) in Boxes(body))
                    if (mediaType == "hdlr" && mediaBody.Length >= 12 && mediaBody.Slice(8, 4).SequenceEqual("vide"u8))
                        isVideo = true;
        }
        return isVideo ? size : null;
    }

    private static (int, int)? ReadTrackSize(ReadOnlySpan<byte> tkhd)
    {
        if (tkhd.Length < 1)
            return null;
        // Width and height close the box, as 16.16 fixed point
        var sizeAt = tkhd[0] == 1 ? 88 : 76;
        if (tkhd.Length < sizeAt + 8)
            return null;
        var width = (int)(BinaryPrimitives.ReadUInt32BigEndian(tkhd[sizeAt..]) >> 16);
        var height = (int)(BinaryPrimitives.ReadUInt32BigEndian(tkhd[(sizeAt + 4)..]) >> 16);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    private static BoxEnumerable Boxes(ReadOnlySpan<byte> content) => new(content);

    private readonly ref struct Box(string type, ReadOnlySpan<byte> body)
    {
        private readonly ReadOnlySpan<byte> _body = body;
        public string Type { get; } = type;

        public void Deconstruct(out string type, out ReadOnlySpan<byte> body)
        {
            type = Type;
            body = _body;
        }
    }

    private readonly ref struct BoxEnumerable(ReadOnlySpan<byte> content)
    {
        private readonly ReadOnlySpan<byte> _content = content;
        public BoxEnumerator GetEnumerator() => new(_content);
    }

    private ref struct BoxEnumerator(ReadOnlySpan<byte> content)
    {
        private ReadOnlySpan<byte> _rest = content;

        public Box Current { get; private set; }

        public bool MoveNext()
        {
            if (_rest.Length < 8)
                return false;
            long size = BinaryPrimitives.ReadUInt32BigEndian(_rest);
            var headerLength = 8;
            if (size == 1)
            {
                if (_rest.Length < 16)
                    return false;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(_rest[8..]);
                headerLength = 16;
            }
            else if (size == 0)
                size = _rest.Length;
            if (size < headerLength || size > _rest.Length)
                return false;
            Current = new Box(Encoding.ASCII.GetString(_rest.Slice(4, 4)), _rest[headerLength..(int)size]);
            _rest = _rest[(int)size..];
            return true;
        }
    }
}
