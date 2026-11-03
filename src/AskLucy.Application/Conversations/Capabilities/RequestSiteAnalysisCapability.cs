using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
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
    IUnitOfWork unitOfWork,
    EffectiveSiteBoundary effectiveSiteBoundary) : IConversationCapability
{
    public const string CapabilityKey = "request_site_analysis";
    private const string Actor = "system:request-site-analysis";

    public string Name => CapabilityKey;

    public string Description =>
        "Starts a multi-specialist analysis of a site — geometry, context, connectivity, environment, " +
        "and character — delivered as findings over the next several seconds to minutes.";

    public string WhenToUse =>
        "Use when the user asks for a site analysis, a full analysis of a site, or to analyze a " +
        "place in depth — \"analyze this site\", \"do a full site analysis\", \"what can you tell " +
        "me about this site\" — for the site currently shown in the viewer, a named site, or explicit coordinates.";

    public string ArgumentHint => "site: the name the user asked for; omit for 'this site'; or lat/long";

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

    /// <summary>
    /// Offerable once a site is on screen (specs/068). An offer is in fact the <i>right</i> way to
    /// reach something this expensive — its duration is <see cref="CapabilityDuration.Extended"/> —
    /// because the user consents before it spends the time, which is the whole argument for
    /// offering rather than acting. Narrower than <see cref="IsAvailable"/> for the same reason as
    /// <see cref="OpenSolarAnalysisCapability"/> — availability stays unconditional so a missing
    /// site is narrated rather than hidden, but there is nothing to offer analysis of.
    /// </summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) =>
        context.HasActiveLocation && !justCompleted.WasInvokedThisTurn(CapabilityKey);

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

        // specs/088 FR-003/FR-004 — the count comes from the same declaration the provisioner
        // publishes branches from. A hand-maintained constant here silently closes the analysis
        // after the first specialist reports, and later findings are rejected as arriving at a
        // finished analysis.
        var analysis = SiteAnalysisAggregate.Create(context.UserId, userChatId, siteName, latitude, longitude, boundaryGeoJson, SiteAnalysisSpecialists.Count, Actor);
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

        // A named site in this turn's own argument — resolved fresh, exactly as SiteBoundaryResolverTool
        // does — UNLESS the argument is really a reference to the site already on screen. The deciding
        // model passes the user's literal words ("this site") — or a JSON null — despite the hint
        // telling it to omit them; geocoding those words found something adjacent and ran the whole
        // boundary pipeline, which exhausted this capability's 90 s budget before dispatch. A JSON
        // null also made GetString() throw, which surfaced as a bare "value provided was null" failure.
        // Anything that is not a real name means the active site.
        if (root.TryGetProperty("site", out var siteEl) &&
            siteEl.ValueKind == JsonValueKind.String &&
            siteEl.GetString() is { } siteQuery &&
            !string.IsNullOrWhiteSpace(siteQuery) &&
            !IsActiveSiteReference(siteQuery))
        {
            return await ResolveByNameAsync(siteQuery.Trim(), userChatId, cancellationToken);
        }

        return await ResolveActiveSiteAsync(userChatId, cancellationToken);
    }

    /// <summary>
    /// "This site", "the current site", "this place", "here" — phrases that name the site already on
    /// screen rather than a place to look up. Compared after trimming, lower-casing and collapsing
    /// whitespace; the set is deliberately small, so a real place called "The Site" still resolves.
    /// </summary>
    private static readonly HashSet<string> ActiveSiteReferences = new(StringComparer.Ordinal)
    {
        "this site", "the site", "current site", "the current site", "active site",
        "selected site", "the selected site", "this place", "the place",
        "this location", "the location", "current location", "the current location", "here",
    };

    internal static bool IsActiveSiteReference(string value)
    {
        var normalized = string.Join(' ', value.Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        normalized = normalized.TrimEnd('.', '!', '?', ',');
        return ActiveSiteReferences.Contains(normalized);
    }

    /// <summary>The conversation's own site: its stored boundary first, then its active location.</summary>
    private async Task<(string SiteName, string SiteLocation, double Latitude, double Longitude, string? BoundaryGeoJson)?> ResolveActiveSiteAsync(
        Guid userChatId, CancellationToken cancellationToken)
    {
        // No argument — fall back to the conversation's already-active site, exactly as OpenSolarAnalysisCapability does.
        var chat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
        if (await effectiveSiteBoundary.ResolveAsync(chat, cancellationToken) is { } boundary)
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
        // SPEC-042's own rule: a reference to the site the conversation already has must not trigger
        // a second resolution. The assistant commonly passes the active site's own name (e.g. the
        // user says "Analyze this site" with "Al Safa Park 2" selected), and re-resolving it costs a
        // geocode plus the whole boundary pipeline — Overpass, imagery and the vision cross-check —
        // which on a slow day exceeds this capability's own 90 s budget and times the turn out
        // before anything is dispatched. The stored boundary is also the one the analysis must use.
        var chat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
        if (await effectiveSiteBoundary.ResolveAsync(chat, cancellationToken) is { } stored &&
            string.Equals(stored.SiteName.Trim(), siteQuery.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (stored.SiteName, stored.SiteName, stored.CentroidLatitude, stored.CentroidLongitude,
                JsonSerializer.Serialize(stored.Polygon));
        }

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
