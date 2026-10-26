using System.Security.Cryptography;

namespace Server.Application.Coordinator.Push;

public static class PushCipher
{
    public const int KeyLength = 32;
    public const int NonceLength = 12;
    public const int TagLength = 16;

    public static string Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext)
    {
        var sealedMessage = new byte[NonceLength + plaintext.Length + TagLength];
        var nonce = sealedMessage.AsSpan(0, NonceLength);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plaintext, sealedMessage.AsSpan(NonceLength, plaintext.Length),
            sealedMessage.AsSpan(NonceLength + plaintext.Length));

        return Convert.ToBase64String(sealedMessage);
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> key, string sealedText)
    {
        var sealedMessage = Convert.FromBase64String(sealedText);
        if (sealedMessage.Length < NonceLength + TagLength)
            throw new CryptographicException("The sealed message is too short.");

        var plaintext = new byte[sealedMessage.Length - NonceLength - TagLength];

        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(sealedMessage.AsSpan(0, NonceLength), sealedMessage.AsSpan(NonceLength, plaintext.Length),
            sealedMessage.AsSpan(NonceLength + plaintext.Length), plaintext);

        return plaintext;
    }
}
