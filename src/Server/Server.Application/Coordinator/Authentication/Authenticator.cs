using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Security;
using Server.Core.Models.User;
using Shared.Enums.Auth;

namespace Server.Application.Coordinator.Authentication;

public class Authenticator : IAuthenticator
{
    private readonly ILogger _logger;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITwoFactorService _twoFactorService;
    private readonly IPasskeyService _passkeyService;
    private readonly IUserRepository _userRepository;
    private readonly IUserSecurityDataRepository _userSecurityDataRepository;
    private readonly IUserPasskeyRepository _userPasskeyRepository;

    // Read on every use: the server password and the name can change while the coordinator runs
    private readonly ICoordinatorInstanceContext _coordinatorInstanceContext;

    // The last one-time code step each user logged in with: a code is good for one login (RFC 6238, 5.2)
    private readonly ConcurrentDictionary<int, long> _usedTimeSteps = new();

    public Authenticator(
        ILogger logger,
        IPasswordHasher passwordHasher,
        ITwoFactorService twoFactorService,
        IPasskeyService passkeyService,
        IUserRepository userRepository,
        IUserSecurityDataRepository userSecurityDataRepository,
        IUserPasskeyRepository userPasskeyRepository,
        ICoordinatorInstanceContext coordinatorInstanceContext)
    {
        _logger = logger;
        _passwordHasher = passwordHasher;
        _twoFactorService = twoFactorService;
        _passkeyService = passkeyService;
        _userRepository = userRepository;
        _userSecurityDataRepository = userSecurityDataRepository;
        _userPasskeyRepository = userPasskeyRepository;
        _coordinatorInstanceContext = coordinatorInstanceContext;
    }

    public bool CheckLoginData(string? rawServerPassword = null)
    {
        var serverPasswordHash = _coordinatorInstanceContext.Model.PasswordHash;
        if (string.IsNullOrEmpty(serverPasswordHash))
            return true;

        if (string.IsNullOrEmpty(rawServerPassword))
            return false;

        return _passwordHasher.VerifyPassword(serverPasswordHash, rawServerPassword);
    }

    public async Task<LoginChallenge?> BeginUserLoginAsync(string userIdentity, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdentityAsync(_coordinatorInstanceContext.Id, userIdentity, ct);
        if (user == null || user.IsSystem || user.IsBanned)
            return null;

        var security = await _userSecurityDataRepository.GetByUserIdAsync(user.Id, ct);
        if (security is not { RequiresSecondStage: true })
            return null;

        var methods = new List<SecondStageAuthType>();
        if (security.IsPasswordEnabled)
            methods.Add(SecondStageAuthType.Password);
        if (security.IsTwoFactorEnabled)
            methods.Add(SecondStageAuthType.TwoFactor);

        PasskeyLoginOptions? passkey = null;
        var hasPasskeys = false;
        if (security.IsFIDO2Enabled)
        {
            var credentials = (await _userPasskeyRepository.GetByUserIdAsync(user.Id, ct))
                .Where(key => key.IsEnabled)
                .Select(key => key.CredentialId)
                .ToList();
            hasPasskeys = credentials.Count > 0;

            if (hasPasskeys && _passkeyService.IsAvailable)
            {
                passkey = _passkeyService.BeginLogin(credentials);
                methods.Add(SecondStageAuthType.FIDO2);
            }
            else if (hasPasskeys)
            {
                // Without the relying party the passkeys cannot be checked, and the user is not let in without them
                _logger.LogWarning("User {UserId} logs in with passkeys, but Fido2:RelyingPartyId is not set.",
                    user.Id);
            }
        }

        // Flagged for passkeys that are all gone: the identity is enough again
        if (methods.Count == 0 && !hasPasskeys)
            return null;

        return new LoginChallenge(user.Id, security.HasKnowledgeFactor, passkey, methods);
    }

    public async Task<bool> CheckUserLoginData(int userId, string? rawUserPassword = null, string? twoFactorCode = null)
    {
        var security = await _userSecurityDataRepository.GetByUserIdAsync(userId);
        // A user with passkeys alone cannot get past them with an empty password and code
        if (security is not { HasKnowledgeFactor: true })
            return false;

        if (security.IsPasswordEnabled)
        {
            if (string.IsNullOrEmpty(rawUserPassword)
                || !_passwordHasher.VerifyPassword(security.PasswordHash, rawUserPassword))
                return false;
        }

        if (security.IsTwoFactorEnabled)
        {
            if (string.IsNullOrEmpty(twoFactorCode)
                || !_twoFactorService.ValidateCode(security.TwoFactorKey!, twoFactorCode, out var timeStep))
                return false;

            // Taken only by a login that succeeds, so a wrong password does not burn the code
            var replayed = false;
            _usedTimeSteps.AddOrUpdate(userId, timeStep, (_, last) =>
            {
                replayed = timeStep <= last;
                return replayed ? last : timeStep;
            });
            if (replayed)
            {
                _logger.LogInformation("User {UserId} sent a one-time code that was used already.", userId);
                return false;
            }
        }

        return true;
    }

    public async Task<bool> CheckPasskeyAsync(int userId, string passkeyOptionsJson, PasskeyAssertion assertion)
    {
        if (!_passkeyService.IsAvailable)
            return false;

        var passkey = await _userPasskeyRepository.GetByCredentialIdAsync(assertion.CredentialId);
        if (passkey is not { IsEnabled: true } || passkey.UserId != userId)
            return false;

        var counter = await _passkeyService.VerifyLoginAsync(passkeyOptionsJson, assertion, passkey.PublicKey,
            passkey.SignatureCounter, userId);
        if (counter == null)
            return false;

        passkey.RecordUse(counter.Value, DateTime.UtcNow);
        await _userPasskeyRepository.UpdateAsync(passkey);
        return true;
    }
}
