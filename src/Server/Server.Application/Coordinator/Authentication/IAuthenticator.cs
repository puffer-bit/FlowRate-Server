using Server.Application.Abstractions.Security;

namespace Server.Application.Coordinator.Authentication;

public interface IAuthenticator
{
    /// <summary>Checks the server password of the coordinator, if it has one.</summary>
    bool CheckLoginData(string? rawServerPassword = null);

    /// <summary>
    /// What a user logging in by username or with an identity of the earlier protocol 3 has to prove. Null when the
    /// user has no password, one-time codes or passkeys.
    /// </summary>
    Task<LoginChallenge?> BeginFactorLoginAsync(int userId, CancellationToken ct = default);

    /// <summary>The password and the one-time code, each if the user has it. False for a user who has neither.</summary>
    Task<bool> CheckUserLoginData(int userId, string? rawUserPassword = null, string? twoFactorCode = null);

    /// <summary>A passkey assertion answering the options of <see cref="BeginFactorLoginAsync"/>.</summary>
    Task<bool> CheckPasskeyAsync(int userId, string passkeyOptionsJson, PasskeyAssertion assertion);
}

/// <param name="AcceptsCodes">A password and/or one-time code completes the login (LoginProofRequest).</param>
/// <param name="Passkey">Options a passkey answers (LoginPasskeyRequest); null when the user has none.</param>
/// <param name="Methods">What the user can prove, for the client to ask for.</param>
public sealed record LoginChallenge(
    int UserId,
    bool AcceptsCodes,
    PasskeyLoginOptions? Passkey,
    IReadOnlyList<Shared.Enums.Auth.LoginFactor> Methods);
