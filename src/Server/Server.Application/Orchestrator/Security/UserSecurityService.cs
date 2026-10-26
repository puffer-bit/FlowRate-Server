using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Security;
using Server.Application.Orchestrator.Settings;
using Server.Core.Models.User;
using Shared.Validation;

namespace Server.Application.Orchestrator.Security;

public sealed class UserSecurityService(
    ICoordinatorSettingsService coordinators,
    IUserRepository users,
    IUserSecurityDataRepository securityData,
    IUserPasskeyRepository passkeys,
    IPasswordHasher passwordHasher,
    ITwoFactorService twoFactor,
    IPasskeyService passkeyService,
    TimeProvider timeProvider,
    ILogger<UserSecurityService> logger) : IUserSecurityService
{
    public const int PasswordMinLength = 8;
    private static readonly TimeSpan RegistrationLifetime = TimeSpan.FromMinutes(5);

    // The options of the registration each user has going; one at a time
    private readonly ConcurrentDictionary<int, (string OptionsJson, DateTimeOffset StartedAt)> _registrations = new();
    // One change of a user's data at a time, so two panels cannot overwrite each other's change
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<UserSecurityOverview?> GetAsync(string coordinatorId, int userId, CancellationToken ct = default)
    {
        if (await FindUserAsync(coordinatorId, userId, ct) == null)
            return null;

        var data = await LoadAsync(userId, ct);
        var keys = await passkeys.GetByUserIdAsync(userId, ct);
        return new UserSecurityOverview(data, keys.OrderBy(key => key.CreatedAt).ToList(), passkeyService.IsAvailable);
    }

    public async Task<bool> CheckProofAsync(int userId, string? password, string? twoFactorCode,
        CancellationToken ct = default)
    {
        var data = await LoadAsync(userId, ct);
        if (data.IsPasswordEnabled
            && (string.IsNullOrEmpty(password) || !passwordHasher.VerifyPassword(data.PasswordHash, password)))
            return false;
        if (data.IsTwoFactorEnabled
            && (string.IsNullOrEmpty(twoFactorCode) || !twoFactor.ValidateCode(data.TwoFactorKey!, twoFactorCode, out _)))
            return false;
        return true;
    }

    public Task<UserSecurityStatus> SetPasswordAsync(string coordinatorId, int userId, string? password,
        CancellationToken ct = default) =>
        ChangeAsync(coordinatorId, userId, ct, data =>
        {
            if (password == null)
            {
                data.RemovePassword();
                return UserSecurityStatus.Done;
            }

            if (password.Length is < PasswordMinLength or > Limits.PasswordMaxLength)
                return UserSecurityStatus.Invalid;

            data.SetPassword(passwordHasher.HashPassword(password)!);
            return UserSecurityStatus.Done;
        });

    public async Task<(UserSecurityStatus Status, TwoFactorOffer? Offer)> OfferTwoFactorAsync(string coordinatorId,
        int userId, CancellationToken ct = default)
    {
        var coordinator = await coordinators.GetAsync(coordinatorId, ct);
        var user = await FindUserAsync(coordinatorId, userId, ct);
        if (coordinator == null || user == null)
            return (UserSecurityStatus.UserNotFound, null);

        TwoFactorOffer? offer = null;
        var status = await ChangeAsync(coordinatorId, userId, ct, data =>
        {
            if (data.IsTwoFactorEnabled)
                return UserSecurityStatus.Invalid;

            var secret = twoFactor.GenerateSecretKey();
            data.OfferTwoFactorKey(secret);
            offer = new TwoFactorOffer(secret, twoFactor.GetQrCodeUri(user.Username, coordinator.Name, secret));
            return UserSecurityStatus.Done;
        });
        return (status, offer);
    }

    public Task<UserSecurityStatus> EnableTwoFactorAsync(string coordinatorId, int userId, string code,
        CancellationToken ct = default) =>
        ChangeAsync(coordinatorId, userId, ct, data =>
        {
            if (!data.IsTwoFactorPending)
                return UserSecurityStatus.Invalid;
            if (!twoFactor.ValidateCode(data.TwoFactorKey!, code, out _))
                return UserSecurityStatus.Refused;

            data.EnableTwoFactor();
            return UserSecurityStatus.Done;
        });

    public Task<UserSecurityStatus> DisableTwoFactorAsync(string coordinatorId, int userId,
        CancellationToken ct = default) =>
        ChangeAsync(coordinatorId, userId, ct, data =>
        {
            data.DisableTwoFactor();
            return UserSecurityStatus.Done;
        });

    public async Task<(UserSecurityStatus Status, string? OptionsJson)> BeginPasskeyRegistrationAsync(
        string coordinatorId, int userId, CancellationToken ct = default)
    {
        if (!passkeyService.IsAvailable)
            return (UserSecurityStatus.Unavailable, null);
        var user = await FindUserAsync(coordinatorId, userId, ct);
        if (user == null)
            return (UserSecurityStatus.UserNotFound, null);

        var known = (await passkeys.GetByUserIdAsync(userId, ct)).Select(key => key.CredentialId).ToList();
        var options = passkeyService.BeginRegistration(userId, user.Username, known);
        _registrations[userId] = (options, timeProvider.GetUtcNow());
        return (UserSecurityStatus.Done, options);
    }

    public async Task<UserSecurityStatus> RegisterPasskeyAsync(string coordinatorId, int userId, string? label,
        string credentialJson, CancellationToken ct = default)
    {
        if (!passkeyService.IsAvailable)
            return UserSecurityStatus.Unavailable;
        if (await FindUserAsync(coordinatorId, userId, ct) == null)
            return UserSecurityStatus.UserNotFound;

        // The options are good for one answer, taken whether it holds or not
        if (!_registrations.TryRemove(userId, out var registration)
            || timeProvider.GetUtcNow() - registration.StartedAt > RegistrationLifetime)
            return UserSecurityStatus.Invalid;

        var credential = await passkeyService.FinishRegistrationAsync(registration.OptionsJson, credentialJson,
            async id => await passkeys.GetByCredentialIdAsync(id, ct) == null, ct);
        if (credential == null)
            return UserSecurityStatus.Refused;

        await _lock.WaitAsync(ct);
        try
        {
            await passkeys.AddAsync(UserPasskey.Create(userId, credential.CredentialId, credential.PublicKey,
                credential.SignatureCounter, label, credential.AaGuid, timeProvider.GetUtcNow().UtcDateTime), ct);
            await SyncPasskeyFlagAsync(userId, ct);
        }
        finally
        {
            _lock.Release();
        }

        logger.LogInformation("A passkey was registered for user {UserId}.", userId);
        return UserSecurityStatus.Done;
    }

    public async Task<UserSecurityStatus> DeletePasskeyAsync(string coordinatorId, int userId, int passkeyId,
        CancellationToken ct = default)
    {
        if (await FindUserAsync(coordinatorId, userId, ct) == null)
            return UserSecurityStatus.UserNotFound;

        await _lock.WaitAsync(ct);
        try
        {
            var passkey = await passkeys.GetByIdAsync(passkeyId, ct);
            if (passkey == null || passkey.UserId != userId)
                return UserSecurityStatus.PasskeyNotFound;

            await passkeys.DeleteAsync(passkeyId, ct);
            await SyncPasskeyFlagAsync(userId, ct);
        }
        finally
        {
            _lock.Release();
        }

        logger.LogInformation("Passkey {PasskeyId} of user {UserId} was removed.", passkeyId, userId);
        return UserSecurityStatus.Done;
    }

    private async Task SyncPasskeyFlagAsync(int userId, CancellationToken ct)
    {
        var data = await LoadAsync(userId, ct);
        data.SetPasskeysEnabled((await passkeys.GetByUserIdAsync(userId, ct)).Any(key => key.IsEnabled));
        await securityData.UpdateAsync(data, ct);
    }

    private async Task<UserSecurityStatus> ChangeAsync(string coordinatorId, int userId, CancellationToken ct,
        Func<UserSecurityData, UserSecurityStatus> change)
    {
        if (await FindUserAsync(coordinatorId, userId, ct) == null)
            return UserSecurityStatus.UserNotFound;

        await _lock.WaitAsync(ct);
        try
        {
            var data = await LoadAsync(userId, ct);
            var status = change(data);
            if (status == UserSecurityStatus.Done)
                await securityData.UpdateAsync(data, ct);
            return status;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<UserSecurityData> LoadAsync(int userId, CancellationToken ct) =>
        await securityData.GetByUserIdAsync(userId, ct) ?? UserSecurityData.CreateEmpty(userId);

    private async Task<UserModel?> FindUserAsync(string coordinatorId, int userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        return user is { IsSystem: false } && user.CoordinatorInstanceId == coordinatorId ? user : null;
    }
}
