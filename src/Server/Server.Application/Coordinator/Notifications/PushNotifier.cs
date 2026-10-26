using Server.Core.Models.Push;
using Shared.Enums;
using Server.Application.Coordinator.Roles;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Push;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Push;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Core.Models.TextChat.Messages;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Mentions;

namespace Server.Application.Coordinator.Notifications;

public sealed class PushNotifier : IEventHandler<ChatSessionMessageAddedEvent>, IDisposable
{
    private const int QueueCapacity = 1000;

    private readonly ILogger _logger;
    private readonly string _coordinatorInstanceId;
    private readonly IPushSender _sender;
    private readonly IPushDeviceRepository _devices;
    private readonly Channel<ChatMessageModel> _queue;
    private readonly CancellationTokenSource _stopping = new();

    private IUserManager? _userManager;
    private IChatSessionManager? _chatSessionManager;
    private ICoordinatorPermissions? _permissions;

    private Task Completion { get; }

    public PushNotifier(ILogger logger, string coordinatorInstanceId, IPushSender sender,
        IPushDeviceRepository devices)
    {
        _logger = logger;
        _coordinatorInstanceId = coordinatorInstanceId;
        _sender = sender;
        _devices = devices;
        _queue = Channel.CreateBounded<ChatMessageModel>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        Completion = sender.IsEnabled ? Task.Run(ProcessAsync) : Task.CompletedTask;
    }

    public void Watch(IUserManager userManager, IChatSessionManager chatSessionManager,
        ICoordinatorPermissions permissions)
    {
        _userManager = userManager;
        _chatSessionManager = chatSessionManager;
        _permissions = permissions;
    }

    public Task HandleAsync(ChatSessionMessageAddedEvent evt, CancellationToken ct = default)
    {
        if (_sender.IsEnabled && !evt.Message.IsSystem)
            _queue.Writer.TryWrite(evt.Message);

        return Task.CompletedTask;
    }
    
    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(_stopping.Token))
            {
                try
                {
                    await NotifyAsync(message, _stopping.Token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Sending push notifications for message {MessageId} failed.",
                        message.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task NotifyAsync(ChatMessageModel message, CancellationToken cancellationToken)
    {
        if (_userManager is not { } userManager || _chatSessionManager is not { } chatSessionManager
            || _permissions is not { } permissions)
            return;

        if (!chatSessionManager.TryGetSession(message.ChatId, out var chat))
            return;

        var devices = await _devices.GetAllAsync(_coordinatorInstanceId, cancellationToken);
        var recipients = new List<PushDeviceModel>();
        foreach (var device in devices.Where(device => device.UserId != message.SenderUserId
                                                       && !userManager.TryGetConnectedUser(device.UserId, out _)))
        {
            // Nothing of a chat reaches a user who may not see it
            if ((await permissions.PermissionsInAsync(device.UserId, chat)).HasFlag(Permission.ViewSessions))
                recipients.Add(device);
        }

        if (recipients.Count == 0)
            return;

        var sender = await userManager.GetUserAsync(message.SenderUserId);
        var mentions = ChatMentions.Find(message.TextContent?.Text);
        var mentionedUserIds = ChatMentions.UserIdsOf(mentions);
        var mentionsEveryone = mentions.Any(mention => mention.Kind == ChatMentionKind.Everyone);
        var text = await RenderMentionsAsync(userManager, message.TextContent?.Text, mentions);
        var payload = PushPayload.Create(_coordinatorInstanceId, chat.Name, sender?.Username ?? "",
            sender?.AvatarUrl, message, text, isMention: false);
        var mentionPayload = mentions.Count == 0
            ? payload
            : PushPayload.Create(_coordinatorInstanceId, chat.Name, sender?.Username ?? "",
                sender?.AvatarUrl, message, text, isMention: true);

        foreach (var device in recipients)
        {
            var isMentioned = mentionsEveryone || mentionedUserIds.Contains(device.UserId);
            var result = await _sender.SendAsync(
                new PushMessage(device.Token, device.Platform, device.KeyId, PushCipher.Encrypt(device.Key, isMentioned ? mentionPayload : payload)),
                cancellationToken);

            switch (result.Status)
            {
                case PushSendStatus.TokenInvalid:
                    await _devices.RemoveAsync(_coordinatorInstanceId, device.Token, cancellationToken);
                    _logger.LogDebug("Removed the push device of user {UserId}: {Detail}", device.UserId, result.Detail);
                    break;
                case PushSendStatus.Failed:
                    _logger.LogWarning("Push to user {UserId} failed: {Detail}", device.UserId, result.Detail);
                    break;
            }
        }
    }

    private static async Task<string?> RenderMentionsAsync(IUserManager userManager, string? text,
        IReadOnlyList<ChatMentionToken> mentions)
    {
        if (text == null || mentions.Count == 0)
            return text;

        var builder = new StringBuilder(text.Length);
        var position = 0;
        foreach (var mention in mentions)
        {
            builder.Append(text, position, mention.Start - position);
            builder.Append('@');
            builder.Append(mention.Kind switch
            {
                ChatMentionKind.Everyone => "everyone",
                ChatMentionKind.Here => "here",
                _ => (await userManager.GetUserAsync(mention.UserId))?.Username ?? mention.UserId.ToString()
            });
            position = mention.End;
        }

        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }

    public Task DrainAsync()
    {
        _queue.Writer.TryComplete();
        return Completion;
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _stopping.Cancel();
    }
}
