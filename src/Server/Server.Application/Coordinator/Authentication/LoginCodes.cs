using System.Security.Cryptography;

namespace Server.Application.Coordinator.Authentication;

/// <summary>One-time login codes the control panel gives out: twelve characters, three groups of four.</summary>
public static class LoginCodes
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Length = 12;

    public static string Create()
    {
        var chars = new char[Length];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        var code = new string(chars);
        return $"{code[..4]}-{code[4..8]}-{code[8..]}";
    }

    /// <summary>The hash a code is kept and looked up by, whatever its case and separators.</summary>
    public static byte[] Hash(string code) =>
        SecretHash.Of(new string(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray()));
}
