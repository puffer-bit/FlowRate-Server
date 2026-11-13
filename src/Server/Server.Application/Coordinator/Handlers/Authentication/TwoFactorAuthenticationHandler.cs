using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models.Requests.Auth;

namespace Server.Application.Coordinator.Handlers.Authentication;

/// <summary>Finishes a login with the user's password and/or one-time code.</summary>
public class TwoFactorAuthenticationHandler(IAuthenticator authenticator, SecondStageAuthentication secondStage)
    : IMessageHandler<TwoFactorUserAuthRequest>
{
    public Task<HandleMessageResult> HandleMessage(TwoFactorUserAuthRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        return secondStage.HandleAsync(request, connectionId,
            login => login.Challenge.AcceptsCodes
                ? authenticator.CheckUserLoginData(login.Challenge.UserId, request.Password, request.TwoFactorCode)
                : Task.FromResult(false),
            (user, token) => request.ToResponse(user.ToDto(user.Id), token));
    }
}
