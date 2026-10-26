using System.Buffers.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Application.Abstractions.Security;
using Server.Application.Options;
using Server.Infrastructure.Options;

namespace Server.Infrastructure.Security;

/// <summary>WebAuthn ceremonies on fido2-net-lib. User verification (PIN, biometrics) is always required.</summary>
public sealed class PasskeyService : IPasskeyService
{
    private readonly Fido2? _fido2;
    private readonly Fido2Configuration? _configuration;
    private readonly ILogger<PasskeyService> _logger;

    public PasskeyService(IOptions<Fido2Options> options, IOptions<OrchestratorOptions> orchestrator,
        ILogger<PasskeyService> logger)
    {
        _logger = logger;
        var fido2 = options.Value;
        if (!fido2.IsEnabled)
            return;

        var origins = fido2.Origins.Count > 0
            ? fido2.Origins.Select(origin => origin.TrimEnd('/')).ToHashSet()
            : [$"https://{fido2.RelyingPartyId}:{orchestrator.Value.Port}"];

        _configuration = new Fido2Configuration
        {
            ServerDomain = fido2.RelyingPartyId!,
            ServerName = fido2.RelyingPartyName,
            Origins = origins,
            TimestampDriftTolerance = 300_000
        };
        _fido2 = new Fido2(_configuration, metadataService: null!);
    }

    public bool IsAvailable => _fido2 != null;

    public string BeginRegistration(int userId, string userName, IReadOnlyList<byte[]> knownCredentialIds)
    {
        var fido2 = Require();
        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = PasskeyUserHandle.Of(userId), Name = userName, DisplayName = userName },
            ExcludeCredentials = knownCredentialIds.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Preferred,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None
        });
        return options.ToJson();
    }

    public async Task<PasskeyRegistration?> FinishRegistrationAsync(string optionsJson, string attestationResponseJson,
        Func<byte[], Task<bool>> isCredentialIdFree, CancellationToken ct = default)
    {
        var fido2 = Require();
        try
        {
            var options = CredentialCreateOptions.FromJson(optionsJson);
            var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationResponseJson);
            if (response == null)
                return null;

            var credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = (args, _) => isCredentialIdFree(args.CredentialId)
            }, ct);

            return new PasskeyRegistration(credential.Id, credential.PublicKey, credential.SignCount, credential.AaGuid);
        }
        catch (Exception ex) when (ex is Fido2VerificationException or JsonException or ArgumentException
                                       or FormatException or InvalidOperationException)
        {
            _logger.LogInformation("A passkey registration was refused: {Reason}", ex.Message);
            return null;
        }
    }

    public PasskeyLoginOptions BeginLogin(IReadOnlyList<byte[]> allowedCredentialIds)
    {
        var fido2 = Require();
        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowedCredentialIds.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            UserVerification = UserVerificationRequirement.Required
        });

        return new PasskeyLoginOptions(
            options.ToJson(),
            Base64Url.EncodeToString(options.Challenge),
            options.RpId ?? _configuration!.ServerDomain,
            options.Timeout,
            allowedCredentialIds.Select(id => Base64Url.EncodeToString(id)).ToList(),
            "required");
    }

    public async Task<uint?> VerifyLoginAsync(string optionsJson, PasskeyAssertion assertion, byte[] storedPublicKey,
        uint storedSignatureCounter, int userId, CancellationToken ct = default)
    {
        var fido2 = Require();
        var userHandle = PasskeyUserHandle.Of(userId);
        try
        {
            var result = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = new AuthenticatorAssertionRawResponse
                {
                    Id = Base64Url.EncodeToString(assertion.CredentialId),
                    RawId = assertion.CredentialId,
                    Type = PublicKeyCredentialType.PublicKey,
                    Response = new AuthenticatorAssertionRawResponse.AssertionResponse
                    {
                        AuthenticatorData = assertion.AuthenticatorData,
                        Signature = assertion.Signature,
                        ClientDataJson = assertion.ClientDataJson,
                        UserHandle = assertion.UserHandle
                    },
                    ClientExtensionResults = new AuthenticationExtensionsClientOutputs()
                },
                OriginalOptions = AssertionOptions.FromJson(optionsJson),
                StoredPublicKey = storedPublicKey,
                StoredSignatureCounter = storedSignatureCounter,
                // Discoverable credentials name their user; it has to be the one logging in
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                    Task.FromResult(args.UserHandle is null || args.UserHandle.AsSpan().SequenceEqual(userHandle))
            }, ct);

            return result.SignCount;
        }
        catch (Exception ex) when (ex is Fido2VerificationException or JsonException or ArgumentException
                                       or FormatException or InvalidOperationException)
        {
            _logger.LogInformation("A passkey login of user {UserId} was refused: {Reason}", userId, ex.Message);
            return null;
        }
    }

    private Fido2 Require() =>
        _fido2 ?? throw new InvalidOperationException("Passkeys are not configured: set Fido2:RelyingPartyId.");
}
