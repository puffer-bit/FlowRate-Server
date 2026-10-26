using Server.Application.Coordinator.Roles;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Core.Models.Push;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Content;
using Shared.Models.Push;
using Shared.Models.Requests.SessionActions.ChatSession.Models;
using Shared.Validation;

namespace Server.Application.Coordinator.Push;

public enum PushReplyResult
{
    Sent,
    Invalid,
    Unauthorized,
    Forbidden,
    ChatNotExists,
    TooManyRequests,
    InternalError
}

public sealed class PushReplyService
{
    private readonly ILogger _logger;
    private readonly IPushDeviceRepository _devices;
    private readonly IUserManager _users;
    private readonly IChatSessionManager _chats;
    private readonly ChatReadService _reads;
    private readonly RequestRateLimits _limits;
    private readonly TimeProvider _time;
    private readonly string _coordinatorInstanceId;
    private readonly ConcurrentDictionary<int, TokenBucket> _buckets = new();
    private int _bucketsVersion;
    private ICoordinatorPermissions? _permissions;

    public PushReplyService(ILogger logger, IPushDeviceRepository devices, IUserManager users,
        IChatSessionManager chats, ChatReadService reads, RequestRateLimits limits, string coordinatorInstanceId,
        TimeProvider? time = null)
    {
        _logger = logger;
        _devices = devices;
        _users = users;
        _chats = chats;
        _reads = reads;
        _limits = limits;
        _bucketsVersion = limits.Version;
        _time = time ?? TimeProvider.System;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    /// <summary>Starts checking replies against the permissions of the user in the chat.</summary>
    public void Watch(ICoordinatorPermissions permissions) => _permissions = permissions;

    public async Task<PushReplyResult> ReplyAsync(string replyToken, PushReplyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!MessageValidator.TryValidate(request, out _))
            return PushReplyResult.Invalid;

        try
        {
            var (authorization, device) = await AuthorizeAsync(replyToken, cancellationToken);
            if (authorization != PushReplyResult.Sent)
                return authorization;

            const Permission write = Permission.ViewSessions | Permission.SendMessages;
            if (_permissions is { } permissions && _chats.TryGetSession(request.ChatId, out var chat)
                && (await permissions.PermissionsInAsync(device!.UserId, chat) & write) != write)
                return PushReplyResult.Forbidden;

            var message = new NewChatMessageModel
            {
                ChatId = request.ChatId,
                TextContent = new ChatTextContentDTO { Text = request.Text }
            };
            if (!MessageValidator.TryValidate(message, out _))
                return PushReplyResult.Invalid;

            return await _chats.AddMessageAsync(request.ChatId, message, device!.UserId) switch
            {
                AddMessageResult.NoError => PushReplyResult.Sent,
                AddMessageResult.ChatNotExists => PushReplyResult.ChatNotExists,
                AddMessageResult.Rejected => PushReplyResult.Forbidden,
                _ => PushReplyResult.InternalError
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Replying to a push notification failed.");
            return PushReplyResult.InternalError;
        }
    }

    public async Task<PushReplyResult> MarkReadAsync(string replyToken, PushMarkReadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!MessageValidator.TryValidate(request, out _))
            return PushReplyResult.Invalid;

        try
        {
            var (authorization, device) = await AuthorizeAsync(replyToken, cancellationToken);
            if (authorization != PushReplyResult.Sent)
                return authorization;

            var (result, _) = await _reads.MarkReadAsync(device!.UserId, request.ChatId, request.MessageId,
                cancellationToken);
            return result switch
            {
                ChatReadResult.NoError => PushReplyResult.Sent,
                ChatReadResult.ChatNotExists => PushReplyResult.ChatNotExists,
                _ => PushReplyResult.InternalError
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Marking a chat read from a push notification failed.");
            return PushReplyResult.InternalError;
        }
    }

    private async Task<(PushReplyResult result, PushDeviceModel? device)> AuthorizeAsync(string replyToken,
        CancellationToken cancellationToken)
    {
        var device = await _devices.FindByReplyTokenAsync(_coordinatorInstanceId,
            PushReplyToken.Hash(replyToken), cancellationToken);
        if (device == null)
            return (PushReplyResult.Unauthorized, null);

        // Buckets made from older limits are dropped, so new limits start full
        var version = _limits.Version;
        if (Interlocked.Exchange(ref _bucketsVersion, version) != version)
            _buckets.Clear();

        var limit = _limits.For(RequestRateGroup.ChatWrite);
        var bucket = _buckets.GetOrAdd(device.UserId,
            _ => new TokenBucket(limit.Capacity, limit.RefillPerSecond, _time));
        if (!bucket.TryTake())
            return (PushReplyResult.TooManyRequests, null);

        var user = await _users.GetUserAsync(device.UserId);
        if (user is not { State: UserState.Normal })
            return (PushReplyResult.Forbidden, null);

        return (PushReplyResult.Sent, device);
    }
}
