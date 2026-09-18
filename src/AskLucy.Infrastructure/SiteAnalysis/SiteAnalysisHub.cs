using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.SiteAnalysis;

/// <summary>
/// Real-time push of site-analysis chat notices (specs/057-site-analysis-agent research.md D5,
/// contracts/site-analysis-hub-events.md). Mirrors <c>PanelHub</c>/<c>AgentExecutionHub</c>/
/// <c>MemoryHub</c>/<c>DocumentProcessingHub</c> exactly — the caller joins a single group keyed by
/// their own server-verified user id (from the auth token, never client-supplied), so a push is
/// only ever delivered to the user whose analysis triggered it (FR-012).
/// </summary>
[Authorize]
public sealed class SiteAnalysisHub : Hub
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
