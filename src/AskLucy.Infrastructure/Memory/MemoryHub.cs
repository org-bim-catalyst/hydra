using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Memory;

/// <summary>
/// Real-time push for memory notifications (spec.md FR-006a, research.md Decision 11). Mirrors
/// <c>DocumentProcessingHub</c>'s server-verified per-user-group join exactly — the caller joins a
/// group keyed by their own server-assigned user id (from the auth token, never client-supplied),
/// so a user only ever receives their own memory events.
/// </summary>
[Authorize]
public sealed class MemoryHub : Hub
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
