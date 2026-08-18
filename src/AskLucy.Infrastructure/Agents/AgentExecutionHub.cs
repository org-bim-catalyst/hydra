using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Agents;

/// <summary>
/// Real-time push for agent execution progress (spec.md FR-034, contracts/agent-execution-events.md).
/// Mirrors <c>MemoryHub</c>/<c>DocumentProcessingHub</c> exactly — the caller joins a single group
/// keyed by their own server-verified user id (from the auth token, never client-supplied), not a
/// per-execution group, so one connection carries every one of the caller's concurrent executions'
/// events; the client filters by <c>executionId</c> in the payload.
/// </summary>
[Authorize]
public sealed class AgentExecutionHub : Hub
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
