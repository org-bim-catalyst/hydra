using AskLucy.Application.Abstractions;
using AskLucy.Application.Panels;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Panels;

/// <summary>Pure push over <see cref="PanelHub"/> (contracts/panel-hub-events.md) — no persistence,
/// mirroring <c>AgentExecutionNotifier</c>'s push-only shape (panel layout/state is client-session
/// only, spec Assumption).</summary>
public sealed class PanelNotifier(IHubContext<PanelHub> hubContext) : IPanelNotifier
{
    public Task PanelRequestedAsync(string userId, PanelRequestDto request, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(PanelHub.UserGroup(userId)).SendAsync("PanelRequested", request, cancellationToken);
}
