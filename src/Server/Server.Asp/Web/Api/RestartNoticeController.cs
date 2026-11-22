using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Server.Application.Orchestrator.Restart;
using Shared.Enums;
using Shared.Validation;

namespace Server.Asp.Web.Api;

/// <summary>
/// Warns the users of every coordinator before a restart, an update or a shutdown. The warning only tells; the
/// server stops when it is stopped, or by itself at the time for a restart that asks for it.
/// </summary>
[ApiController]
[Route(PanelRoutes.Api + "/restart-notice")]
[Produces("application/json")]
public class RestartNoticeController(IServerRestartNotice notice, TimeProvider time) : ControllerBase
{
    public const int MessageMaxLength = 500;
    public const int DelayMaxMinutes = 24 * 60;

    public sealed record RestartNoticeDto(ServerRestartReason Reason, DateTimeOffset At, string? Message, bool RestartsAtEnd);

    /// <param name="DelayMinutes">How long the users have until then; a restart at once is <see cref="RestartNow"/>.</param>
    /// <param name="RestartsAtEnd">The server stops itself then (only for a restart); its supervisor starts it again.</param>
    public sealed record AnnounceRestartDto(
        ServerRestartReason Reason,
        [Range(1, DelayMaxMinutes)] int DelayMinutes,
        [StringLength(MessageMaxLength), NoControlCharacters(allowLineBreaks: true)] string? Message = null,
        bool RestartsAtEnd = false);

    [HttpGet]
    [ProducesResponseType<RestartNoticeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult<RestartNoticeDto> Get() =>
        notice.Current is { } current ? ToDto(current) : NoContent();

    [HttpPost]
    [ProducesResponseType<RestartNoticeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<RestartNoticeDto> Announce(AnnounceRestartDto request)
    {
        if (!Enum.IsDefined(request.Reason))
            return Problem(statusCode: 400, title: "Unknown reason.");
        if (request.RestartsAtEnd && request.Reason != ServerRestartReason.Restart)
            return Problem(statusCode: 400, title: "Only a restart can be done by the server itself.");

        var announcement = new ServerRestartAnnouncement(request.Reason,
            time.GetUtcNow().AddMinutes(request.DelayMinutes),
            string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(), request.RestartsAtEnd);
        notice.Announce(announcement);
        return ToDto(announcement);
    }

    public sealed record RestartNowDto(
        [StringLength(MessageMaxLength), NoControlCharacters(allowLineBreaks: true)] string? Message = null);

    /// <summary>
    /// Stops the server at once for its supervisor to start it again. The users get only the going-down notice,
    /// not a warning just before it.
    /// </summary>
    [HttpPost("now")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult RestartNow(RestartNowDto request)
    {
        notice.RestartNow(string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim());
        return NoContent();
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Cancel() => notice.Cancel() ? NoContent() : NotFound();

    private static RestartNoticeDto ToDto(ServerRestartAnnouncement announcement) =>
        new(announcement.Reason, announcement.At, announcement.Message, announcement.RestartsAtEnd);
}
