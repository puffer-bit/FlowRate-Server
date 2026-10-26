using OtpNet;
using Server.Application.Abstractions.Security;

namespace Server.Infrastructure.Security;

public sealed class TwoFactorService : ITwoFactorService
{
    // 160 bits, as RFC 4226 recommends
    private const int SecretSize = 20;
    private static readonly VerificationWindow Window = new(previous: 1, future: 1);

    public string GenerateSecretKey() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(SecretSize));

    public string GetQrCodeUri(string accountName, string issuer, string secretKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountName);
        ArgumentException.ThrowIfNullOrEmpty(issuer);
        ArgumentException.ThrowIfNullOrEmpty(secretKey);

        var label = $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}";
        return $"otpauth://totp/{label}?secret={secretKey}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";
    }

    public bool ValidateCode(string secretKey, string clientCode, out long timeStep)
    {
        timeStep = 0;
        if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(clientCode))
            return false;

        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(secretKey));
            return totp.VerifyTotp(clientCode.Trim(), out timeStep, Window);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
