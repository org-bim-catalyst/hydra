using System.Text.Json;
using AskLucy.Domain.SiteAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Application.SiteAnalysis;

/// <summary>
/// Runs <see cref="SiteAnalysisResultRelay"/> inside its own DI scope, mirroring
/// <c>ScopeIsolatedLocationResolutionService</c> exactly and for the identical reason: multiple
/// specialists report concurrently (<c>WorkflowExecutionOrchestrator.ExecuteParallelAsync</c>'s
/// branches run via <c>Task.WhenAll</c>, sharing the workflow job's one scoped <c>DbContext</c>
/// per that method's own doc comment — "neither is thread-safe"). Without this, two specialists
/// finishing close together would race on the same <c>DbContext</c> instance and risk the exact
/// "A second operation was started on this context before a previous operation completed"
/// failure already observed in production for the concurrent-location-resolution case
/// (<c>ScopeIsolatedLocationResolutionService</c>'s own history). No existing <see
/// cref="Agents.Tools.IAgentTool"/> writes to the database at all — this relay is the first, so
/// this isolation has no precedent to lean on within the tool layer itself; it borrows the
/// pattern instead.
/// <para>
/// Kept as a decorator, not an <see cref="IServiceScopeFactory"/> call inside every specialist
/// tool, for the same reason the location-resolution precedent gives: the isolation travels with
/// the interface, so a specialist tool and its tests stay unaware of scoping entirely.
/// </para>
/// </summary>
public sealed class ScopeIsolatedSiteAnalysisResultRelay(IServiceScopeFactory scopeFactory) : ISiteAnalysisResultRelay
{
    public async Task ReportSuccessAsync(
        Guid siteAnalysisId,
        SiteAnalysisType analysisType,
        SiteAnalysisResultMetadata metadata,
        JsonDocument content,
        Guid? documentId,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var inner = scope.ServiceProvider.GetRequiredService<SiteAnalysisResultRelay>();
        await inner.ReportSuccessAsync(siteAnalysisId, analysisType, metadata, content, documentId, cancellationToken);
    }

    public async Task ReportFailureAsync(
        Guid siteAnalysisId,
        SiteAnalysisType analysisType,
        string failureReason,
        Exception? cause,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var inner = scope.ServiceProvider.GetRequiredService<SiteAnalysisResultRelay>();
        await inner.ReportFailureAsync(siteAnalysisId, analysisType, failureReason, cause, cancellationToken);
    }
}
