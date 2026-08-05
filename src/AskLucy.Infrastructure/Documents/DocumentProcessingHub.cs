using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Documents;

/// <summary>
/// Real-time push for document processing status and notifications (FR-027, FR-047,
/// research.md Decision 7). On connect, the caller joins a group keyed by their own
/// server-assigned user id (from the auth token, never client-supplied) so a user only ever
/// receives their own documents' events; administrators additionally join the
/// <see cref="AdminDashboardGroup"/> for the aggregate view (FR-045a). Lives in
/// <c>Infrastructure</c>, not <c>Web</c>, so <c>ProcessingNotifier</c> (also Infrastructure) can
/// inject <c>IHubContext&lt;DocumentProcessingHub&gt;</c> directly without Infrastructure
/// depending on Web (constitution §3 Dependency Rule) — <c>Web</c>'s <c>Program.cs</c> maps this
/// hub type the same way it already depends on other Infrastructure types.
/// </summary>
[Authorize]
public sealed class DocumentProcessingHub : Hub
{
    public const string AdminDashboardGroup = "admin-dashboard";

    public static string UserGroup(string userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("No user id claim present.");

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        if (Context.User?.IsInRole("Administrator") == true || Context.User?.IsInRole("Super User") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminDashboardGroup);
        }

        await base.OnConnectedAsync();
    }
}
