namespace Server.Application.Coordinator.Media;

public static class MediaSignatures
{
    public const int HeaderLength = 12;

    public static bool Matches(string mimeType, ReadOnlySpan<byte> header) => mimeType switch
    {
        "image/jpeg" => header.StartsWith<byte>([0xFF, 0xD8, 0xFF]),
        "image/png" => header.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/gif" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
        "image/webp" => header.Length >= HeaderLength && header.StartsWith("RIFF"u8) && header[8..].StartsWith("WEBP"u8),
        _ => false
    };
}
