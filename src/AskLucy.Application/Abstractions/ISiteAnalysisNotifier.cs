using AskLucy.Application.SiteAnalysis;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Push of site-analysis chat notices to the caller's own browser session
/// (specs/057-site-analysis-agent research.md D5, contracts/site-analysis-hub-events.md). The
/// concrete delivery mechanism is a SignalR hub (<c>SiteAnalysisHub</c>) implemented in
/// <c>Infrastructure</c> — Application/Domain never reference SignalR directly (constitution
/// &#167;3). Exists only because chat has no SignalR hub of its own — it streams over SSE per turn,
/// so a background job has no other way to make a message appear in an open conversation. The
/// finding's <b>panel</b> half of delivery continues to use the existing <see cref="IPanelNotifier"/>
/// unchanged.
/// </summary>
public interface ISiteAnalysisNotifier
{
    Task ResultReceivedAsync(string userId, SiteAnalysisResultReceivedDto payload, CancellationToken cancellationToken = default);

    Task AnalysisCompletedAsync(string userId, SiteAnalysisCompletedDto payload, CancellationToken cancellationToken = default);
}
