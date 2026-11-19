using System.Buffers.Binary;

namespace Server.Application.Coordinator.Media;

/// <summary>
/// Reads the width and height of an image from its header, without decoding it. The orientation of a
/// JPEG is not applied: the sides may be swapped, which does not change the longest one.
/// </summary>
public static class ImageDimensions
{
    // Enough for the header of PNG, GIF and WebP
    private const int HeaderLength = 30;

    /// <returns>Null when the content is not an image of that type, or its header is damaged.</returns>
    public static async Task<(int Width, int Height)?> ReadAsync(Stream content, string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (mimeType == "image/jpeg")
            return await ReadJpegAsync(content, cancellationToken);

        var header = new byte[HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        return Read(mimeType, header.AsSpan(0, read));
    }

    private static (int, int)? Read(string mimeType, ReadOnlySpan<byte> header) => mimeType switch
    {
        "image/png" => header.Length >= 24 && header[12..16].SequenceEqual("IHDR"u8)
            ? Positive((int)BinaryPrimitives.ReadUInt32BigEndian(header[16..]),
                (int)BinaryPrimitives.ReadUInt32BigEndian(header[20..]))
            : null,
        "image/gif" => header.Length >= 10
            ? Positive(BinaryPrimitives.ReadUInt16LittleEndian(header[6..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[8..]))
            : null,
        "image/webp" => ReadWebP(header),
        _ => null
    };

    private static (int, int)? ReadWebP(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength || !header.StartsWith("RIFF"u8) || !header[8..12].SequenceEqual("WEBP"u8))
            return null;

        var chunk = header[12..16];
        var data = header[20..];

        // Lossy: a key frame starts with a start code, then 14-bit sides
        if (chunk.SequenceEqual("VP8 "u8))
            return data[3] == 0x9D && data[4] == 0x01 && data[5] == 0x2A
                ? Positive(BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 0x3FFF,
                    BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 0x3FFF)
                : null;

        // Lossless: a signature byte, then the sides less one in 14 bits each
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (data[0] != 0x2F)
                return null;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
            return Positive((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        // Extended: flags, three reserved bytes, then the canvas sides less one in 24 bits each
        if (chunk.SequenceEqual("VP8X"u8))
            return Positive((data[4] | data[5] << 8 | data[6] << 16) + 1, (data[7] | data[8] << 8 | data[9] << 16) + 1);

        return null;
    }

    // The sides are in the first start-of-frame segment, after the metadata segments
    private static async Task<(int, int)?> ReadJpegAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new byte[9];

        if (!await ReadExactlyAsync(content, buffer.AsMemory(0, 2), cancellationToken) || buffer[0] != 0xFF || buffer[1] != 0xD8)
            return null;

        while (true)
        {
            if (!await ReadExactlyAsync(content, buffer.AsMemory(0, 2), cancellationToken) || buffer[0] != 0xFF)
                return null;

            var marker = buffer[1];

            // Fill bytes before a marker
            while (marker == 0xFF)
            {
                if (!await ReadExactlyAsync(content, buffer.AsMemory(1, 1), cancellationToken))
                    return null;
                marker = buffer[1];
            }

            // Markers without a length; the image data starts with SOS before any frame header is a damaged file
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                continue;
            if (marker is 0xD9 or 0xDA)
                return null;

            if (!await ReadExactlyAsync(content, buffer.AsMemory(0, 2), cancellationToken))
                return null;

            var length = BinaryPrimitives.ReadUInt16BigEndian(buffer) - 2;

            if (length < 0)
                return null;

            // SOF0-SOF15 but DHT (C4), JPG (C8) and DAC (CC): precision, height, width
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 5 || !await ReadExactlyAsync(content, buffer.AsMemory(0, 5), cancellationToken))
                    return null;
                return Positive(BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(3)),
                    BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(1)));
            }

            if (!await SkipAsync(content, length, cancellationToken))
                return null;
        }
    }

    private static async Task<bool> ReadExactlyAsync(Stream content, Memory<byte> buffer, CancellationToken cancellationToken) =>
        await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken) == buffer.Length;

    private static async Task<bool> SkipAsync(Stream content, int count, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            if (content.Position + count > content.Length)
                return false;
            content.Seek(count, SeekOrigin.Current);
            return true;
        }

        var buffer = new byte[Math.Min(count, 8192)];
        while (count > 0)
        {
            var read = await content.ReadAsync(buffer.AsMemory(0, Math.Min(count, buffer.Length)), cancellationToken);
            if (read == 0)
                return false;
            count -= read;
        }

        return true;
    }

    private static (int, int)? Positive(int width, int height) => width > 0 && height > 0 ? (width, height) : null;
}
