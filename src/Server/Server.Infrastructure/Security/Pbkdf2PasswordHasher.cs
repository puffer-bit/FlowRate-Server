using System.Security.Cryptography;
using Server.Application.Abstractions.Security;

namespace Server.Infrastructure.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16; 
    private const int KeySize = 32;
    private const int Iterations = 600_000;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    private const char Delimiter = ';';

    public string? HashPassword(string? rawPassword)
    {
        if (string.IsNullOrEmpty(rawPassword))
            return null;

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            rawPassword,
            salt,
            Iterations,
            HashAlgorithm,
            KeySize);

        string saltBase64 = Convert.ToBase64String(salt);
        string hashBase64 = Convert.ToBase64String(hash);

        return $"{saltBase64}{Delimiter}{hashBase64}";
    }

    public bool VerifyPassword(string? hashedPassword, string? rawPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(rawPassword))
            return false;

        string[] parts = hashedPassword.Split(Delimiter);
        if (parts.Length != 2)
            return false;

        try
        {
            byte[] salt = Convert.FromBase64String(parts[0]);
            byte[] originalHash = Convert.FromBase64String(parts[1]);

            byte[] inputHash = Rfc2898DeriveBytes.Pbkdf2(
                rawPassword,
                salt,
                Iterations,
                HashAlgorithm,
                KeySize);

            return CryptographicOperations.FixedTimeEquals(originalHash, inputHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
