using Shared.Enums;

namespace Server.Application.Coordinator.Roles;

/// <summary>
/// What a request needs before it is handled.
/// </summary>
/// <param name="Required">The permissions the sender must have; none for a request anyone may send.</param>
/// <param name="SessionId">
/// The session the request is about: the sender's permissions there count, after its overrides. The host of a
/// hybrid session is allowed the request too, when <see cref="RequestAuthorizer.HostPermissions"/> cover what is
/// required.
/// </param>
/// <param name="TargetUserId">Another user the request acts on, who must rank below the sender.</param>
public sealed record RequestPolicy(Permission Required, int? SessionId = null, int? TargetUserId = null)
{
    public static RequestPolicy Open { get; } = new(Permission.None);

    public static RequestPolicy Require(Permission permission) => new(permission);

    public static RequestPolicy InSession(int sessionId, Permission permission) => new(permission, sessionId);

    public RequestPolicy Against(int targetUserId) => this with { TargetUserId = targetUserId };
}
