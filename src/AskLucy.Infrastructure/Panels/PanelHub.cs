using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Panels;

/// <summary>
/// Real-time push of AI-requested floating panels (spec 028 FR-001, contracts/panel-hub-events.md).
/// Mirrors <c>AgentExecutionHub</c>/<c>MemoryHub</c>/<c>DocumentProcessingHub</c> exactly — the
/// caller joins a single group keyed by their own server-verified user id (from the auth token,
/// never client-supplied), so a <c>PanelRequested</c> push is only ever delivered to the user whose
/// AI interaction triggered it (FR-023, panels are private per user).
/// </summary>
[Authorize]
public sealed class PanelHub : Hub
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
