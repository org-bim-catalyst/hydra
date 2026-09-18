using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteAnalysis;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.SiteAnalysis;

/// <summary>Pure push over <see cref="SiteAnalysisHub"/> (contracts/site-analysis-hub-events.md) — no persistence, mirroring <c>PanelNotifier</c>'s push-only shape.</summary>
public sealed class SiteAnalysisNotifier(IHubContext<SiteAnalysisHub> hubContext) : ISiteAnalysisNotifier
{
    public Task ResultReceivedAsync(string userId, SiteAnalysisResultReceivedDto payload, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(SiteAnalysisHub.UserGroup(userId)).SendAsync("SiteAnalysisResultReceived", payload, cancellationToken);

    public Task AnalysisCompletedAsync(string userId, SiteAnalysisCompletedDto payload, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(SiteAnalysisHub.UserGroup(userId)).SendAsync("SiteAnalysisCompleted", payload, cancellationToken);
}
