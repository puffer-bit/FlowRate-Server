namespace Server.Application.Abstractions.Security;

/// <summary>Time-based one-time codes (TOTP, RFC 6238) as authenticator apps make them.</summary>
public interface ITwoFactorService
{
    /// <summary>A new random secret, Base32.</summary>
    string GenerateSecretKey();

    /// <summary>The otpauth:// URI authenticator apps read from a QR code.</summary>
    string GetQrCodeUri(string accountName, string issuer, string secretKey);

    /// <summary>
    /// Checks a code against the secret, allowing one step of clock drift either way. <paramref name="timeStep"/>
    /// tells which step the code belongs to, so a code that was used once can be refused the second time.
    /// </summary>
    bool ValidateCode(string secretKey, string clientCode, out long timeStep);
}
