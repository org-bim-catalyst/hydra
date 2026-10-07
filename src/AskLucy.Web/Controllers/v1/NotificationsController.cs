using AskLucy.Application.Common;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Commands.DeleteNotification;
using AskLucy.Application.Notifications.Commands.MarkAllNotificationsRead;
using AskLucy.Application.Notifications.Commands.MarkNotificationRead;
using AskLucy.Application.Notifications.Queries.GetNotification;
using AskLucy.Application.Notifications.Queries.GetNotifications;
using AskLucy.Application.Notifications.Queries.GetUnreadNotificationCount;
using AskLucy.Domain.Notifications;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AskLucy.Web.Localization;

namespace AskLucy.Web.Controllers.v1;

/// <summary>The notification center (US1, contracts/notifications-api.md). Every resource is scoped to the caller.</summary>
[ApiController]
[LocalizedSurface]
[Authorize]
[EnableRateLimiting("notifications-endpoints")]
[Route("api/v1/notifications")]
public sealed class NotificationsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationListItemDto>>> GetNotifications(
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 25,
        [FromQuery(Name = "category")] IReadOnlyList<NotificationCategory>? category = null,
        [FromQuery] NotificationReadState state = NotificationReadState.All,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetNotificationsQuery(cursor, limit, category, state), cancellationToken));

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadNotificationCountResponse>> GetUnreadCount(CancellationToken cancellationToken) =>
        Ok(new UnreadNotificationCountResponse(await mediator.Send(new GetUnreadNotificationCountQuery(), cancellationToken)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NotificationDetailDto>> GetNotification(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetNotificationQuery(id), cancellationToken));

    [HttpPost("{id:guid}/actions/mark-read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new MarkNotificationReadCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("actions/mark-all-read")]
    public async Task<ActionResult<MarkAllNotificationsReadResponse>> MarkAllRead(MarkAllNotificationsReadRequest? request, CancellationToken cancellationToken)
    {
        var updated = await mediator.Send(new MarkAllNotificationsReadCommand(request?.Category), cancellationToken);
        return Ok(new MarkAllNotificationsReadResponse(updated));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteNotificationCommand(id), cancellationToken);
        return NoContent();
    }
}
