using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Server.Application.Coordinator.Push;
using Shared.Models.Push;
using Shared.Serialization;
using Shared.Validation;

namespace Server.Infrastructure.Coordinator.Push;

public interface IPushHttpEndpoints
{
    void Map(IEndpointRouteBuilder endpoints);
}

public sealed class PushHttpEndpoints : IPushHttpEndpoints
{
    private const string BearerPrefix = "Bearer ";
    private const int MaxBodyLength = Limits.ChatTextMaxLength * 4 + 1024;
    private static readonly int MaxTokenLength = (Limits.PushReplyTokenLength + 2) / 3 * 4;

    private readonly PushReplyService _replies;

    public PushHttpEndpoints(PushReplyService replies)
    {
        _replies = replies;
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(PushEndpoints.Reply, (RequestDelegate)ReplyAsync);
        endpoints.MapPost(PushEndpoints.MarkRead, (RequestDelegate)MarkReadAsync);
    }

    private Task ReplyAsync(HttpContext context) =>
        HandleAsync(context, ProtocolJsonContext.Default.PushReplyRequest,
            (token, request, ct) => _replies.ReplyAsync(token, request, ct));

    private Task MarkReadAsync(HttpContext context) =>
        HandleAsync(context, ProtocolJsonContext.Default.PushMarkReadRequest,
            (token, request, ct) => _replies.MarkReadAsync(token, request, ct));

    private static async Task HandleAsync<TRequest>(HttpContext context, JsonTypeInfo<TRequest> typeInfo,
        Func<string, TRequest, CancellationToken, Task<PushReplyResult>> handle) where TRequest : class
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var token = header[BearerPrefix.Length..].Trim();
        if (token.Length is 0 || token.Length > MaxTokenLength)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (context.Request.ContentLength is not { } length || length > MaxBodyLength)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        TRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(context.Request.Body, typeInfo, context.RequestAborted);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request == null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        context.Response.StatusCode = await handle(token, request, context.RequestAborted) switch
        {
            PushReplyResult.Sent => StatusCodes.Status204NoContent,
            PushReplyResult.Invalid => StatusCodes.Status400BadRequest,
            PushReplyResult.Unauthorized => StatusCodes.Status401Unauthorized,
            PushReplyResult.Forbidden => StatusCodes.Status403Forbidden,
            PushReplyResult.ChatNotExists => StatusCodes.Status404NotFound,
            PushReplyResult.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError
        };
    }
}
