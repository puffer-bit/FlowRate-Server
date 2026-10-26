using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

public class UserSearchHandler : IMessageHandler<CoordinatorUserSearchRequest>
{
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;

    public UserSearchHandler(IMessageSender messageSender, IUserManager userManager)
    {
        _messageSender = messageSender;
        _userManager = userManager;
    }

    public async Task<HandleMessageResult> HandleMessage(CoordinatorUserSearchRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var query = request.Query?.Trim() ?? string.Empty;
        var users = (await _userManager.GetAllUsersAsync()).Values
            .Where(user => !user.IsSystem && !user.IsBanned)
            .Select(user => (user, rank: RankOf(user.Username, query)))
            .Where(match => match.rank >= 0)
            .OrderBy(match => match.rank)
            .ThenBy(match => _userManager.TryGetConnectedUser(match.user.Id, out _) ? 0 : 1)
            .ThenBy(match => match.user.Username, StringComparer.OrdinalIgnoreCase)
            .Take(request.Limit)
            .Select(match => match.user.ToDto(request.SenderId))
            .ToList();

        _messageSender.SendMessageById(connectionId, request.ToResponse(users, UsersRequestResult.NoError));
        return HandleMessageResult.NoError;
    }

    public BaseMessage? CreateFailureResponse(CoordinatorUserSearchRequest request) =>
        request.ToResponse(new List<UserDTO>(), UsersRequestResult.InternalError);

    private static int RankOf(string username, string query)
    {
        if (query.Length == 0 || username.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 0;

        return username.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : -1;
    }
}
