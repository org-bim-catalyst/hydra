using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// Server-to-client pushes for the notification center (contracts/notification-hub.md). The caller
/// joins the group keyed by their own server-assigned user id, never a client-supplied one. There are
/// no client-invocable methods (research R1): every mutation goes through the REST API.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    public static string UserGroup(string userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("No user id claim present.");

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        await base.OnConnectedAsync();
    }
}
