using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Server.Application.Coordinator.Authentication;

/// <summary>
/// Secrets the server hands out and gets back: nonces, login codes, and the identities and session tokens of the
/// earlier protocol 3. They are random, so a plain SHA-256 is enough to keep a copy of the database from logging
/// anyone in.
/// </summary>
public static class SecretHash
{
    public const int TokenBytes = 32;

    public static string CreateToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    public static byte[] Of(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public static byte[] Of(byte[] bytes) => SHA256.HashData(bytes);
}
