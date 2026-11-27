using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Mappers;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Coordinator;

namespace Server.Application.Coordinator.Handlers.Users;

/// <summary>
/// The login codes of bots: like the codes the control panel gives, but valid for a day, since the code is put into
/// the configuration of the bot rather than typed at once.
/// </summary>
public sealed class BotLoginCodes(string coordinatorInstanceId, IUserLoginCodeRepository loginCodes,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    /// <summary>A new code for the bot; the unused ones it had stop working.</summary>
    public async Task<BotLoginCodeDTO> IssueAsync(int botId)
    {
        var code = LoginCodes.Create();
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + Lifetime;
        await loginCodes.DeleteByUserAsync(coordinatorInstanceId, botId);
        await loginCodes.AddAsync(coordinatorInstanceId, botId, LoginCodes.Hash(code), now, expiresAt);
        return new BotLoginCodeDTO(code, expiresAt);
    }
}

/// <summary>The bots of the coordinator.</summary>
public sealed class BotsHandler(IMessageSender messageSender, IUserManager userManager)
    : IMessageHandler<CoordinatorBotsRequest>
{
    public async Task<HandleMessageResult> HandleMessage(CoordinatorBotsRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var bots = (await userManager.GetBotsAsync())
            .Select(bot => bot.ToDto(request.SenderId))
            .ToList();
        messageSender.SendMessageById(connectionId, request.ToResponse(bots));
        return HandleMessageResult.NoError;
    }
}

/// <summary>Makes a bot owned by the sender and answers its first login code.</summary>
public sealed class CreateBotHandler(ILogger logger, IMessageSender messageSender, IUserManager userManager,
    BotLoginCodes codes) : IMessageHandler<CoordinatorCreateBotRequest>
{
    public async Task<HandleMessageResult> HandleMessage(CoordinatorCreateBotRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        var (error, bot) = await userManager.CreateBot(request.SenderId, request.Username, request.IsDebug);
        if (error != null || bot == null)
        {
            logger.LogTrace("User {SenderId} could not make a bot: {Error}", request.SenderId, error);
            messageSender.SendMessageById(connectionId, request.ToError(error ?? ErrorCode.Internal));
            return HandleMessageResult.NoError;
        }

        var code = await codes.IssueAsync(bot.Id);
        messageSender.SendMessageById(connectionId, request.ToResponse(bot.ToDto(request.SenderId), code));
        return HandleMessageResult.NoError;
    }
}

/// <summary>A new login code for a bot, ending its devices on request.</summary>
public sealed class BotLoginCodeHandler(ILogger logger, IMessageSender messageSender, IUserManager userManager,
    UserSessions devices, BotLoginCodes codes) : IMessageHandler<CoordinatorBotLoginCodeRequest>
{
    public async Task<HandleMessageResult> HandleMessage(CoordinatorBotLoginCodeRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (await BotTarget.FindAsync(userManager, request.TargetUserId) is not { } bot)
        {
            messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.UserNotFound));
            return HandleMessageResult.NoError;
        }

        var code = await codes.IssueAsync(bot.Id);
        if (request.EndDevices)
            await devices.EndAllButAsync(bot.Id, keep: null);

        logger.LogInformation("User {SenderId} gave the bot {BotId} a new login code{Ended}.", request.SenderId, bot.Id,
            request.EndDevices ? " and ended its devices" : string.Empty);
        messageSender.SendMessageById(connectionId, request.ToResponse(code));
        return HandleMessageResult.NoError;
    }
}

/// <summary>Deletes a bot as <see cref="DeleteUserHandler"/> deletes a user.</summary>
public sealed class DeleteBotHandler(ILogger logger, IMessageSender messageSender, IUserManager userManager,
    IConnectionManager connectionManager) : IMessageHandler<CoordinatorDeleteBotRequest>
{
    public async Task<HandleMessageResult> HandleMessage(CoordinatorDeleteBotRequest? request, string connectionId)
    {
        if (request == null)
            return HandleMessageResult.ForbiddenRequest;

        if (await BotTarget.FindAsync(userManager, request.TargetUserId) is not { } bot)
        {
            messageSender.SendMessageById(connectionId, request.ToError(ErrorCode.UserNotFound));
            return HandleMessageResult.NoError;
        }

        var connections = connectionManager.GetUserConnections(bot.Id);
        var result = await userManager.DeleteUser(bot.Id);
        messageSender.SendMessageById(connectionId, request.Answer(result, request.ToResponse));
        if (result != null)
        {
            logger.LogTrace("User {SenderId} failed to delete the bot {Id}: {Result}", request.SenderId, bot.Id, result);
            return HandleMessageResult.NoError;
        }

        DeleteUserHandler.CloseDeleted(connections, bot.Id);
        return HandleMessageResult.NoError;
    }
}

internal static class BotTarget
{
    /// <summary>The bot with this id; null for a person or no one.</summary>
    public static async Task<UserModel?> FindAsync(IUserManager users, int userId) =>
        await users.GetUserAsync(userId) is { IsBot: true } bot ? bot : null;
}
