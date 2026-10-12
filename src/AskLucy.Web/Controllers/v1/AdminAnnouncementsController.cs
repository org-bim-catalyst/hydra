using AskLucy.Application.Notifications.Admin;
using AskLucy.Application.Notifications.Commands.PublishSystemAnnouncement;
using AskLucy.Application.Notifications.Queries.GetSystemAnnouncements;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using AskLucy.Web.Localization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// System announcements (specs/067 FR-004a, contracts/admin-notifications-api.md). Immutable once published: there is no edit
/// and no delete, because an announcement is never a marketing channel.
/// </summary>
[ApiController]
[LocalizedSurface]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/notifications/announcements")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
public sealed class AdminAnnouncementsController(ISender mediator) : ControllerBase
{
    [ProducesResponseType<AdminPage<AdminAnnouncementDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [HttpGet]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<AdminPage<AdminAnnouncementDto>>> GetAnnouncements(
        [FromQuery] string? cursor, [FromQuery] int limit = 25, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetSystemAnnouncementsQuery(cursor, limit), cancellationToken));

    [ProducesResponseType<PublishedAnnouncementDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [HttpPost]
    [RequirePermission("admin.notifications.manage")]
    public async Task<IActionResult> Publish([FromBody] PublishAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new PublishSystemAnnouncementCommand(
                request.Kind, request.Title, request.Message, request.Audience, request.TargetRoleIds, request.IsCritical, request.EndsAtUtc),
            cancellationToken);
        return Created($"/api/v1/admin/notifications/announcements#{result.Id}", result);
    }
}
