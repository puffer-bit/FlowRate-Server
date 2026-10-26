using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Application.Abstractions.Security;
using Server.Application.Options;
using Server.Infrastructure.Options;
using Server.Infrastructure.Security;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Server.Infrastructure.Tests.Security;

public class PasskeyServiceTests : IDisposable
{
    private const int UserId = 7;
    private const string RpId = "localhost";
    private const string Origin = "https://localhost:7114";

    private readonly PasskeyService _service = Create(RpId);
    private readonly SoftwareAuthenticator _authenticator = new(RpId, Origin);

    private static PasskeyService Create(string? rpId, params string[] origins) => new(
        MsOptions.Create(new Fido2Options { RelyingPartyId = rpId, Origins = [.. origins] }),
        MsOptions.Create(new OrchestratorOptions { Port = 7114 }),
        NullLogger<PasskeyService>.Instance);

    [Fact]
    public void WithoutARelyingParty_PasskeysAreNotAvailable()
    {
        var service = Create(null);

        Assert.False(service.IsAvailable);
        Assert.Throws<InvalidOperationException>(() => service.BeginLogin([]));
    }

    [Fact]
    public async Task Registration_ThenLogin_Holds()
    {
        var registration = await RegisterAsync();
        Assert.NotNull(registration);
        Assert.Equal(_authenticator.CredentialId, registration.CredentialId);

        var counter = await LoginAsync(registration);

        Assert.Equal(2u, counter);
    }

    [Fact]
    public async Task Registration_OfACredentialThatIsTaken_IsRefused()
    {
        var options = _service.BeginRegistration(UserId, "user", []);

        var registration = await _service.FinishRegistrationAsync(options, _authenticator.Create(options),
            _ => Task.FromResult(false));

        Assert.Null(registration);
    }

    [Fact]
    public async Task Registration_FromAnotherOrigin_IsRefused()
    {
        _authenticator.OriginOverride = "https://evil.example";

        Assert.Null(await RegisterAsync());
    }

    [Fact]
    public async Task Registration_WithoutUserVerification_IsRefused()
    {
        _authenticator.VerifiesUser = false;

        Assert.Null(await RegisterAsync());
    }

    [Fact]
    public async Task Registration_AnsweringOtherOptions_IsRefused()
    {
        var answered = _service.BeginRegistration(UserId, "user", []);
        var other = _service.BeginRegistration(UserId, "user", []);

        Assert.Null(await _service.FinishRegistrationAsync(other, _authenticator.Create(answered),
            _ => Task.FromResult(true)));
    }

    [Fact]
    public async Task Login_AnsweringOtherOptions_IsRefused()
    {
        var registration = (await RegisterAsync())!;
        var answered = _service.BeginLogin([registration.CredentialId]);
        var other = _service.BeginLogin([registration.CredentialId]);
        var (clientData, authData, signature) = _authenticator.Get(answered.Challenge);

        var counter = await _service.VerifyLoginAsync(other.OptionsJson,
            new PasskeyAssertion(registration.CredentialId, clientData, authData, signature, null),
            registration.PublicKey, registration.SignatureCounter, UserId);

        Assert.Null(counter);
    }

    [Fact]
    public async Task Login_WithAForgedSignature_IsRefused()
    {
        var registration = (await RegisterAsync())!;
        var options = _service.BeginLogin([registration.CredentialId]);
        var (clientData, authData, signature) = _authenticator.Get(options.Challenge);
        signature[^1] ^= 0xFF;

        var counter = await _service.VerifyLoginAsync(options.OptionsJson,
            new PasskeyAssertion(registration.CredentialId, clientData, authData, signature, null),
            registration.PublicKey, registration.SignatureCounter, UserId);

        Assert.Null(counter);
    }

    [Fact]
    public async Task Login_NamingAnotherUser_IsRefused()
    {
        var registration = (await RegisterAsync())!;
        var options = _service.BeginLogin([registration.CredentialId]);
        var (clientData, authData, signature) = _authenticator.Get(options.Challenge);

        var counter = await _service.VerifyLoginAsync(options.OptionsJson,
            new PasskeyAssertion(registration.CredentialId, clientData, authData, signature,
                PasskeyUserHandle.Of(UserId + 1)),
            registration.PublicKey, registration.SignatureCounter, UserId);

        Assert.Null(counter);
    }

    [Fact]
    public async Task Login_WithACounterThatDidNotGrow_IsRefused()
    {
        var registration = (await RegisterAsync())!;

        // The stored counter is ahead of the authenticator: a cloned key
        Assert.Null(await LoginAsync(registration with { SignatureCounter = 100 }));
    }

    [Fact]
    public void LoginOptions_AreBase64UrlAndAskForUserVerification()
    {
        var options = _service.BeginLogin([[1, 2, 3]]);

        Assert.Equal(RpId, options.RelyingPartyId);
        Assert.Equal("required", options.UserVerification);
        Assert.Equal(["AQID"], options.AllowedCredentialIds);
        Assert.DoesNotContain('=', options.Challenge);
    }

    private Task<PasskeyRegistration?> RegisterAsync()
    {
        var options = _service.BeginRegistration(UserId, "user", []);
        return _service.FinishRegistrationAsync(options, _authenticator.Create(options), _ => Task.FromResult(true));
    }

    private Task<uint?> LoginAsync(PasskeyRegistration registration)
    {
        var options = _service.BeginLogin([registration.CredentialId]);
        var (clientData, authData, signature) = _authenticator.Get(options.Challenge);
        return _service.VerifyLoginAsync(options.OptionsJson,
            new PasskeyAssertion(registration.CredentialId, clientData, authData, signature,
                PasskeyUserHandle.Of(UserId)),
            registration.PublicKey, registration.SignatureCounter, UserId);
    }

    public void Dispose() => _authenticator.Dispose();
}
