using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Retrieval;

/// <summary>
/// Real-time push for knowledge-base indexing status (FR-014, FR-039, research.md Decision 7) —
/// mirrors <c>DocumentProcessingHub</c> (specs/015). On connect, the caller joins a group keyed by
/// their own server-assigned user id (from the auth token, never client-supplied) so a user only
/// ever receives their own knowledge bases' events. Lives in <c>Infrastructure</c>, not <c>Web</c>,
/// so <see cref="RetrievalIndexingNotifier"/> (also Infrastructure) can inject
/// <c>IHubContext&lt;RetrievalIndexingHub&gt;</c> directly without <c>Infrastructure</c> depending
/// on <c>Web</c> (constitution §3 Dependency Rule).
/// </summary>
[Authorize]
public sealed class RetrievalIndexingHub : Hub
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
