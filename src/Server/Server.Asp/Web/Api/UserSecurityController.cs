using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Server.Application.Orchestrator.Security;
using Server.Asp.Web.Api.Contracts;

namespace Server.Asp.Web.Api;

/// <summary>
/// Passwords, one-time codes and passkeys of a coordinator user, for the browser control panel. The panel sets
/// them up and takes them away when a user lost a device; users then log in with them.
/// </summary>
[ApiController]
[Route(PanelRoutes.Coordinator + "/users/{userId:int}/security")]
[Produces("application/json")]
public class UserSecurityController(IUserSecurityService security) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserSecurityDto>> Get(string id, int userId, CancellationToken ct)
    {
        var overview = await security.GetAsync(id, userId, ct);
        return overview == null ? NotFound() : ToDto(overview);
    }

    /// <summary>Sets the password, or removes it when it is null.</summary>
    [HttpPut("password")]
    [ProducesResponseType<UserSecurityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserSecurityDto>> SetPassword(string id, int userId, SetPasswordDto password,
        CancellationToken ct) =>
        await AnswerAsync(id, userId, await security.SetPasswordAsync(id, userId, password.Password, ct), ct);

    /// <summary>Offers a new one-time code secret; it is used once a code made from it is confirmed.</summary>
    [HttpPost("two-factor")]
    [ProducesResponseType<TwoFactorOfferDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TwoFactorOfferDto>> OfferTwoFactor(string id, int userId, CancellationToken ct)
    {
        var (status, offer) = await security.OfferTwoFactorAsync(id, userId, ct);
        return status == UserSecurityStatus.Done ? new TwoFactorOfferDto(offer!.Secret, offer.Uri) : Failure(status);
    }

    [HttpPost("two-factor/confirm")]
    [ProducesResponseType<UserSecurityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserSecurityDto>> EnableTwoFactor(string id, int userId, TwoFactorCodeDto code,
        CancellationToken ct) =>
        await AnswerAsync(id, userId, await security.EnableTwoFactorAsync(id, userId, code.Code, ct), ct);

    [HttpDelete("two-factor")]
    [ProducesResponseType<UserSecurityDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserSecurityDto>> DisableTwoFactor(string id, int userId, CancellationToken ct) =>
        await AnswerAsync(id, userId, await security.DisableTwoFactorAsync(id, userId, ct), ct);

    /// <summary>Options for navigator.credentials.create, as the browser takes them (base64url binary values).</summary>
    [HttpPost("passkeys/options")]
    [ProducesResponseType<object>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BeginPasskeyRegistration(string id, int userId, CancellationToken ct)
    {
        var (status, options) = await security.BeginPasskeyRegistrationAsync(id, userId, ct);
        return status == UserSecurityStatus.Done ? Content(options!, "application/json") : Failure(status);
    }

    [HttpPost("passkeys")]
    [ProducesResponseType<UserSecurityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserSecurityDto>> RegisterPasskey(string id, int userId,
        RegisterPasskeyDto passkey, CancellationToken ct)
    {
        if (passkey.Credential.ValueKind != JsonValueKind.Object)
            return Failure(UserSecurityStatus.Refused);

        return await AnswerAsync(id, userId, await security.RegisterPasskeyAsync(id, userId, passkey.Label,
            passkey.Credential.GetRawText(), ct), ct);
    }

    [HttpDelete("passkeys/{passkeyId:int}")]
    [ProducesResponseType<UserSecurityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserSecurityDto>> DeletePasskey(string id, int userId, int passkeyId,
        CancellationToken ct) =>
        await AnswerAsync(id, userId, await security.DeletePasskeyAsync(id, userId, passkeyId, ct), ct);

    // Changes answer with what the user has now, so the panel shows it without another request
    private async Task<ActionResult<UserSecurityDto>> AnswerAsync(string id, int userId, UserSecurityStatus status,
        CancellationToken ct)
    {
        if (status != UserSecurityStatus.Done)
            return Failure(status);
        var overview = await security.GetAsync(id, userId, ct);
        return overview == null ? NotFound() : ToDto(overview);
    }

    private static UserSecurityDto ToDto(UserSecurityOverview overview) => new(
        overview.Data.IsPasswordEnabled,
        overview.Data.IsTwoFactorEnabled,
        overview.Data.IsTwoFactorPending,
        overview.PasskeysAvailable,
        overview.Passkeys
            .Select(key => new PasskeyDto(key.Id, key.Label, key.CreatedAt, key.LastUsedAt, key.IsEnabled))
            .ToList());

    private ObjectResult Failure(UserSecurityStatus status) => status switch
    {
        UserSecurityStatus.UserNotFound => Problem(statusCode: StatusCodes.Status404NotFound,
            title: "The coordinator has no such user."),
        UserSecurityStatus.PasskeyNotFound => Problem(statusCode: StatusCodes.Status404NotFound,
            title: "The user has no such passkey."),
        UserSecurityStatus.Refused => Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "The code or the passkey does not hold.",
            detail: "Check the clock of the device with the authenticator app, or try the passkey again."),
        UserSecurityStatus.Unavailable => Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Passkeys are not set up on this server.",
            detail: "Set Fido2:RelyingPartyId to the domain the server is reached by."),
        _ => Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "That cannot be done now.",
            detail: "Passwords are 8 to 128 characters. A new one-time code secret needs the codes disabled first, " +
                    "and a passkey needs fresh options: start again.")
    };
}
