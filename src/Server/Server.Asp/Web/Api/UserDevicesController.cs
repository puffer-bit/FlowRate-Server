using Microsoft.AspNetCore.Mvc;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;
using Server.Application.Orchestrator;
using Server.Asp.Web.Api.Contracts;
using Server.Infrastructure.Security;
using Shared.Enums.Auth;

namespace Server.Asp.Web.Api;

/// <summary>
/// The devices of a coordinator user, for the browser control panel: removing one that was lost, and giving out a
/// one-time login code that adds a new device when the user has nothing else left to log in with.
/// </summary>
[ApiController]
[Route(PanelRoutes.Coordinator + "/users/{userId:int}")]
[Produces("application/json")]
public class UserDevicesController(
    IOrchestratorInstance orchestrator,
    IUserRepository users,
    IUserSessionRepository devices,
    IUserLoginCodeRepository loginCodes,
    ServerCertificate certificate,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("devices")]
    public async Task<ActionResult<List<UserDeviceDto>>> GetDevices(string id, int userId, CancellationToken ct)
    {
        if (!await IsUserAsync(id, userId, ct))
            return NotFound();

        var online = orchestrator.GetCoordinator(id)?.Devices is { } live
            ? (await live.GetAsync(userId, null)).Where(device => device.IsOnline).Select(device => device.Id).ToHashSet()
            : [];
        var list = await devices.GetByUserAsync(id, userId, timeProvider.GetUtcNow(), ct);
        return list.Select(device => new UserDeviceDto(device.Id, device.DeviceName, device.DevicePlatform,
                device.AppVersion, device.CreatedAt, device.LastUsedAt, device.ExpiresAt, device.LastAddress,
                device.IsTemporary, device.AddedBy, online.Contains(device.Id)))
            .ToList();
    }

    /// <summary>Removes the device; a connected one is disconnected and has to be added again.</summary>
    [HttpDelete("devices/{deviceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeDevice(string id, int userId, Guid deviceId, CancellationToken ct)
    {
        if (!await IsUserAsync(id, userId, ct))
            return NotFound();

        var removed = orchestrator.GetCoordinator(id)?.Devices is { } live
            ? await live.EndAsync(userId, deviceId)
            : await devices.DeleteAsync(id, userId, deviceId, ct);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// A new one-time login code for the user, shown once: good for one device and 15 minutes. Codes given before and
    /// still unused stop working.
    /// </summary>
    [HttpPost("login-code")]
    [ProducesResponseType<LoginCodeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoginCodeDto>> CreateLoginCode(string id, int userId, CancellationToken ct)
    {
        if (!await IsUserAsync(id, userId, ct))
            return NotFound();

        var code = LoginCodes.Create();
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + LoginCodes.Lifetime;
        await loginCodes.DeleteByUserAsync(id, userId, ct);
        await loginCodes.AddAsync(id, userId, LoginCodes.Hash(code), now, expiresAt, ct);
        var port = orchestrator.GetCoordinatorSnapshots().FirstOrDefault(coordinator => coordinator.Id == id)?.Port;
        return new LoginCodeDto(code, expiresAt, port,
            certificate.Certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256));
    }

    private async Task<bool> IsUserAsync(string coordinatorId, int userId, CancellationToken ct) =>
        await users.GetByIdAsync(userId, ct) is { IsSystem: false } user && user.CoordinatorInstanceId == coordinatorId;
}
