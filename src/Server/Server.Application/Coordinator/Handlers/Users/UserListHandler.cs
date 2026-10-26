using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

public class UserListHandler : IMessageHandler<CoordinatorUserListRequest>
{
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;

    public UserListHandler(IMessageSender messageSender, IUserManager userManager)
    {
        _messageSender = messageSender;
        _userManager = userManager;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorUserListRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var users = (await _userManager.GetAllUsersAsync()).Values
            .Where(user => !user.IsSystem)
            .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Select(user => user.ToDto(request.SenderId, showIp: true))
            .ToList();

        _messageSender.SendMessageById(connectionId, request.ToResponse(users, UsersRequestResult.NoError));
        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(CoordinatorUserListRequest request) =>
        request.ToResponse(new List<UserDTO>(), UsersRequestResult.InternalError);
}
