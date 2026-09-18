using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// specs/057-site-analysis-agent — Lucy's entry point into the Site Analysis Agent (FR-001-FR-004).
/// Resolves the site (reusing spec 042's boundary pipeline, never a third resolution path),
/// creates the <see cref="SiteAnalysisAggregate"/> record, dispatches the fan-out workflow, and
/// returns immediately — it must not await the workflow (research.md D1, SC-001).
/// </summary>
public sealed class RequestSiteAnalysisCapability(
    IUserChatRepository userChatRepository,
    ISiteAnalysisRepository siteAnalysisRepository,
    IGeocodingProvider geocodingProvider,
    IBoundaryResolutionService boundaryResolutionService,
    ISiteAnalysisDispatcher dispatcher,
    IUnitOfWork unitOfWork) : IConversationCapability
{
    public const string CapabilityKey = "request_site_analysis";
    private const string Actor = "system:request-site-analysis";

    /// <summary>data-model.md — how many specialists this release dispatches; grows as follow-up specs add more (FR-031), without any change to this capability.</summary>
    private const int ExpectedResultCount = 1;

    public string Name => CapabilityKey;

    public string Description =>
        "Starts a multi-specialist analysis of a site — geometry, context, connectivity, environment, " +
        "and character — delivered as findings over the next several seconds to minutes.";

    public string WhenToUse =>
        "Use when the user asks for a site analysis, a full analysis of a site, or to analyze a " +
        "place in depth — \"analyze this site\", \"do a full site analysis\", \"what can you tell " +
        "me about this site\" — for the site currently shown in the viewer, a named site, or explicit coordinates.";

    public string ArgumentHint => "optional site name, or omit to use the active site; or explicit latitude/longitude";

    public string UsageGuidance =>
        "This dispatches background work and returns immediately — tell the user the analysis has " +
        "started and name the site, in one short line; do not describe findings yourself, since each " +
        "one arrives separately as its own notice and panel as it completes. If the output reports " +
        "the site could not be identified, say so plainly and do not imply an analysis is running.";

    public string Label => "Run a full site analysis";

    public string OfferDescription => "Analyze this site across geometry, context, connectivity, environment and character.";

    public string AcknowledgementTemplate => "Starting the site analysis.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ExternalNetwork];

    public string InputSchemaJson =>
        """{"type":"object","properties":{"site":{"type":"string"},"latitude":{"type":"number"},"longitude":{"type":"number"}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","required":["started"],"properties":{"started":{"type":"boolean"},"siteName":{"type":"string"},"reason":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Extended;

    public SubAgentArea Area => SubAgentArea.SiteAnalysis;

    /// <summary>Always true — the "site could not be identified" outcome (FR-023) is narrated by Lucy from <see cref="ExecuteAsync"/>'s own output, not hidden by catalog filtering (mirrors <see cref="OpenSolarAnalysisCapability"/>).</summary>
    public bool IsAvailable(TurnContext context) => true;

    /// <summary>Invoked directly when asked; not offered as a suggested next step.</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (context.UserChatId is not { } userChatId)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { started = false, reason = "no-conversation" }));
        }

        var resolution = await ResolveSiteAsync(context, userChatId, input, cancellationToken);
        if (resolution is null)
        {
            // FR-023 — told to the user within the same turn; no analysis is started.
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { started = false, reason = "site-not-identified" }));
        }

        var (siteName, siteLocation, latitude, longitude, boundaryGeoJson) = resolution.Value;

        // Spec Assumptions / edge cases — reuse an in-flight analysis for the same chat and site
        // rather than dispatching a duplicate.
        var existing = await siteAnalysisRepository.FindRunningForSiteAsync(context.UserId, userChatId, siteName, cancellationToken);
        if (existing is not null)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { started = true, siteName }));
        }

        var analysis = SiteAnalysisAggregate.Create(context.UserId, userChatId, siteName, latitude, longitude, boundaryGeoJson, ExpectedResultCount, Actor);
        siteAnalysisRepository.Add(analysis);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var executionId = await dispatcher.DispatchAsync(analysis.Id, context.UserId, siteName, siteLocation, latitude, longitude, cancellationToken);

        // Targeted update, not the tracked aggregate — see ISiteAnalysisRepository.RecordWorkflowExecutionIdAsync's
        // own doc comment for why this diagnostic write must not use optimistic concurrency here.
        await siteAnalysisRepository.RecordWorkflowExecutionIdAsync(analysis.Id, executionId, cancellationToken);

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new { started = true, siteName }));
    }

    private async Task<(string SiteName, string SiteLocation, double Latitude, double Longitude, string? BoundaryGeoJson)?> ResolveSiteAsync(
        AgentToolExecutionContext context, Guid userChatId, JsonDocument input, CancellationToken cancellationToken)
    {
        var root = input.RootElement;

        // FR-001 — explicit coordinates skip geocoding entirely.
        if (root.TryGetProperty("latitude", out var latEl) && latEl.ValueKind == JsonValueKind.Number &&
            root.TryGetProperty("longitude", out var lonEl) && lonEl.ValueKind == JsonValueKind.Number)
        {
            var siteName = root.TryGetProperty("site", out var nameEl) && nameEl.GetString() is { Length: > 0 } explicitName
                ? explicitName
                : "the specified location";
            return (siteName, siteName, latEl.GetDouble(), lonEl.GetDouble(), null);
        }

        // A named site in this turn's own argument — resolved fresh, exactly as SiteBoundaryResolverTool does.
        if (root.TryGetProperty("site", out var siteEl) && siteEl.GetString() is { Length: > 0 } siteQuery)
        {
            return await ResolveByNameAsync(siteQuery, userChatId, cancellationToken);
        }

        // No argument — fall back to the conversation's already-active site, exactly as OpenSolarAnalysisCapability does.
        var chat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
        if (chat?.ActiveBoundary is { } boundary)
        {
            return (boundary.SiteName, boundary.SiteName, boundary.CentroidLatitude, boundary.CentroidLongitude, JsonSerializer.Serialize(boundary.Polygon));
        }

        if (chat?.ActiveLocation is { } location)
        {
            return (location.LocationName, location.LocationName, location.Latitude, location.Longitude, null);
        }

        return null;
    }

    private async Task<(string, string, double, double, string?)?> ResolveByNameAsync(string siteQuery, Guid userChatId, CancellationToken cancellationToken)
    {
        IReadOnlyList<GeocodingCandidate> candidates;
        try
        {
            candidates = await geocodingProvider.SearchAsync(siteQuery, cancellationToken);
        }
        catch (GeocodingProviderUnavailableException)
        {
            return null;
        }

        var winner = candidates.OrderByDescending(c => c.Importance).FirstOrDefault();
        if (winner is null)
        {
            return null;
        }

        var confirmedLocation = new ConfirmedLocationData(winner.Latitude, winner.Longitude, winner.LocationName, winner.Importance);
        var boundaryOutcome = await boundaryResolutionService.ResolveAsync(confirmedLocation, userChatId, cancellationToken);

        if (boundaryOutcome.ConfirmedBoundary is { } boundary)
        {
            return (boundary.SiteName, boundary.SiteName, boundary.CentroidLatitude, boundary.CentroidLongitude, JsonSerializer.Serialize(boundary.Polygon));
        }

        // Boundary resolution came back Unavailable — still a valid point to analyze (FR-002 only requires the site be confirmed, not that a polygon exists).
        return boundaryOutcome.Type == BoundaryResolutionOutcomeType.Unavailable
            ? (winner.LocationName, winner.LocationName, winner.Latitude, winner.Longitude, null)
            : null;
    }
}
