using AskLucy.Application.Notifications.Admin;
using AskLucy.Application.Notifications.Commands.BulkRetryNotificationDeliveries;
using AskLucy.Application.Notifications.Commands.RetryNotificationDelivery;
using AskLucy.Application.Notifications.Queries.GetNotificationAudit;
using AskLucy.Application.Notifications.Queries.GetNotificationChannels;
using AskLucy.Application.Notifications.Queries.GetNotificationDeliveries;
using AskLucy.Application.Notifications.Queries.GetNotificationDelivery;
using AskLucy.Application.Notifications.Queries.GetNotificationStatistics;
using AskLucy.Domain.Notifications;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using AskLucy.Web.Localization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Administrators' view of the notification hub (specs/067 US6, contracts/admin-notifications-api.md): statistics, channel
/// health, deliveries and the audit trail. Reading needs <c>admin.notifications.view</c> and acting needs
/// <c>admin.notifications.manage</c>; each endpoint checks its own, so holding manage doesn't quietly grant more than it names.
/// </summary>
[ApiController]
[LocalizedSurface]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/notifications")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
public sealed class AdminNotificationsController(ISender mediator) : ControllerBase
{
    [ProducesResponseType<NotificationStatisticsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [HttpGet("statistics")]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<NotificationStatisticsDto>> GetStatistics(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetNotificationStatisticsQuery(from, to), cancellationToken));

    [ProducesResponseType<IReadOnlyList<NotificationChannelDto>>(StatusCodes.Status200OK)]
    [HttpGet("channels")]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<IReadOnlyList<NotificationChannelDto>>> GetChannels(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetNotificationChannelsQuery(), cancellationToken));

    [ProducesResponseType<AdminPage<AdminDeliveryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [HttpGet("deliveries")]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<AdminPage<AdminDeliveryDto>>> GetDeliveries(
        [FromQuery(Name = "status")] IReadOnlyList<DeliveryStatus>? status,
        [FromQuery] NotificationChannel? channel,
        [FromQuery] NotificationCategory? category,
        [FromQuery] string? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetNotificationDeliveriesQuery(status, channel, category, type, from, to, cursor, limit), cancellationToken));

    [ProducesResponseType<AdminDeliveryDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [HttpGet("deliveries/{deliveryId:guid}")]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<AdminDeliveryDetailDto>> GetDelivery(Guid deliveryId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetNotificationDeliveryQuery(deliveryId), cancellationToken));

    [ProducesResponseType<RetryDeliveryResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [HttpPost("deliveries/{deliveryId:guid}/actions/retry")]
    [RequirePermission("admin.notifications.manage")]
    public async Task<IActionResult> RetryDelivery(Guid deliveryId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RetryNotificationDeliveryCommand(deliveryId), cancellationToken);
        return Accepted(new RetryDeliveryResponse(result.DeliveryId, result.Status));
    }

    [ProducesResponseType<BulkRetryResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [HttpPost("deliveries/actions/retry")]
    [RequirePermission("admin.notifications.manage")]
    public async Task<ActionResult<BulkRetryResult>> BulkRetry([FromBody] BulkRetryDeliveriesRequest request, CancellationToken cancellationToken)
    {
        var filter = request.Filter is null
            ? null
            : new BulkRetryFilter(request.Filter.Status, request.Filter.Channel, request.Filter.From, request.Filter.To);
        return Ok(await mediator.Send(new BulkRetryNotificationDeliveriesCommand(request.DeliveryIds, filter), cancellationToken));
    }

    [ProducesResponseType<AdminPage<AdminAuditEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [HttpGet("audit")]
    [RequirePermission("admin.notifications.view")]
    public async Task<ActionResult<AdminPage<AdminAuditEntryDto>>> GetAudit(
        [FromQuery] NotificationAuditAction? action,
        [FromQuery] string? targetType,
        [FromQuery] string? targetId,
        [FromQuery] string? actorUserId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetNotificationAuditQuery(action, targetType, targetId, actorUserId, from, to, cursor, limit), cancellationToken));
}
