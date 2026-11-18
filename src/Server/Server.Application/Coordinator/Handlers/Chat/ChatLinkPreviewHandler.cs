using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.LinkPreviews;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Mappers;
using Shared.Enums;
using Shared.Models;
using Shared.Models.Requests.SessionActions.ChatSession;

namespace Server.Application.Coordinator.Handlers.Chat;

/// <summary>
/// Answers the composer with the preview of the address being typed. Fetching a page takes a while, and
/// the requests of a connection are handled one after another, so the answer is sent when the page is
/// known and the next request goes on meanwhile.
/// </summary>
public sealed class ChatLinkPreviewHandler(IChatSessionManager chats, LinkPreviewService previews,
    IMessageSender sender, ILogger logger) : IMessageHandler<ChatLinkPreviewRequest>
{
    public Task<HandleMessageResult> HandleMessage(ChatLinkPreviewRequest? request, string connectionId)
    {
        if (request == null)
            return Task.FromResult(HandleMessageResult.ForbiddenRequest);

        if (!chats.TryGetSession(request.ChatId, out _))
        {
            sender.SendMessageById(connectionId, request.ToError(ErrorCode.ChatNotFound));
            return Task.FromResult(HandleMessageResult.InternalError);
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            sender.SendMessageById(connectionId, request.ToError(ErrorCode.InvalidValue));
            return Task.FromResult(HandleMessageResult.InternalError);
        }

        _ = AnswerAsync(request, url, connectionId);
        return Task.FromResult(HandleMessageResult.NoError);
    }

    private async Task AnswerAsync(ChatLinkPreviewRequest request, Uri url, string connectionId)
    {
        BaseMessage answer;
        try
        {
            var preview = await previews.GetAsync(url, request.SenderId);
            answer = request.ToResponse(preview?.ToDto());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Answering a link preview request failed.");
            answer = request.ToError(ErrorCode.Internal);
        }

        sender.SendMessageById(connectionId, answer);
    }
}
