using System.Buffers.Binary;

namespace Server.Infrastructure.Coordinator.LinkPreviews;

/// <summary>
/// The format and size of an image, read from its first bytes. What a server says the type is does not
/// count: only JPEG, PNG, GIF and WebP pass, which every client can show.
/// </summary>
public static class ImageHeader
{
    public static bool TryRead(ReadOnlySpan<byte> data, out string mimeType, out int width, out int height)
    {
        mimeType = string.Empty;
        width = height = 0;

        if (data.Length >= 24 && data.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            mimeType = "image/png";
            width = BinaryPrimitives.ReadInt32BigEndian(data[16..]);
            height = BinaryPrimitives.ReadInt32BigEndian(data[20..]);
            return Valid(width, height);
        }

        if (data.Length >= 10 && (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)))
        {
            mimeType = "image/gif";
            width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
            return Valid(width, height);
        }

        if (data.Length >= 30 && data.StartsWith("RIFF"u8) && data[8..].StartsWith("WEBP"u8))
        {
            mimeType = "image/webp";
            return TryReadWebP(data, out width, out height) && Valid(width, height);
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            mimeType = "image/jpeg";
            return TryReadJpeg(data, out width, out height) && Valid(width, height);
        }

        return false;
    }

    private static bool Valid(int width, int height) =>
        width > 0 && height > 0 && width <= Shared.Validation.Limits.ChatImageMaxSide
        && height <= Shared.Validation.Limits.ChatImageMaxSide;

    private static bool TryReadWebP(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        var chunk = data[12..16];
        if (chunk.SequenceEqual("VP8X"u8))
        {
            width = 1 + (data[24] | data[25] << 8 | data[26] << 16);
            height = 1 + (data[27] | data[28] << 8 | data[29] << 16);
            return true;
        }
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // The key frame starts with 9D 01 2A, then 14 bits of width and of height
            if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A)
                return false;
            width = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF;
            return true;
        }
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (data[20] != 0x2F)
                return false;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            return true;
        }
        return false;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        var position = 2;
        while (position + 9 < data.Length)
        {
            if (data[position] != 0xFF)
                return false;
            var marker = data[position + 1];
            // Fill bytes before a marker
            if (marker == 0xFF)
            {
                position++;
                continue;
            }
            // Markers without a length
            if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7)
            {
                position += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
            // Start of frame, any kind but the DHT, JPG and DAC markers that share the range
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 5)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 7)..]);
                return true;
            }
            if (marker == 0xDA || length < 2)
                return false;
            position += 2 + length;
        }
        return false;
    }
}
