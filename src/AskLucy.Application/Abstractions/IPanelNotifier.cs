using AskLucy.Application.Panels;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Push of an AI-requested floating panel to the caller's own browser session (spec 028 FR-001,
/// contracts/panel-hub-events.md, research.md Decision 2). The concrete delivery mechanism is a
/// SignalR hub (<c>PanelHub</c>) implemented in <c>Infrastructure</c> — Application/Domain never
/// reference SignalR directly (constitution §3). Unlike <see cref="IAgentExecutionNotifier"/>'s
/// deliberately summarized events, <see cref="PanelRequestDto.Data"/> intentionally carries the
/// full structured panel content — that content is the feature being delivered, not execution
/// telemetry about it.
/// </summary>
public interface IPanelNotifier
{
    Task PanelRequestedAsync(string userId, PanelRequestDto request, CancellationToken cancellationToken = default);
}
