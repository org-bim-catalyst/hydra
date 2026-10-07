using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Commands.UpdateNotificationPreferences;
using AskLucy.Application.Notifications.Queries.GetNotificationPreferences;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AskLucy.Web.Localization;

namespace AskLucy.Web.Controllers.v1;

/// <summary>The caller's notification preferences (US4, contracts/notifications-api.md). Always the caller's own.</summary>
[ApiController]
[LocalizedSurface]
[Authorize]
[EnableRateLimiting("notifications-endpoints")]
[Route("api/v1/users/me/notification-preferences")]
public sealed class NotificationPreferencesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationPreferencesDto>> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetNotificationPreferencesQuery(), cancellationToken));

    [HttpPut]
    public async Task<ActionResult<NotificationPreferencesDto>> Update(
        [FromBody] UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        var changes = (request.Changes ?? [])
            .Select(c => new NotificationPreferenceChange(c.Category, c.Channel, c.Enabled, c.Frequency))
            .ToList();
        return Ok(await mediator.Send(new UpdateNotificationPreferencesCommand(changes), cancellationToken));
    }
}
