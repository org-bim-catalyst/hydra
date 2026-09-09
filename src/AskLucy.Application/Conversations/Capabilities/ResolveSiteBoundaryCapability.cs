using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Outlines the site around a confirmed location (specs/045 FR-013, FR-046).
///
/// <para>
/// <b>The expensive one.</b> It reaches Overpass and, when that is inconclusive, a vision
/// cross-check — tens of seconds in the worst case. Until specs/045 it ran as a hidden side
/// effect of geocoding, so a user watched a finished-looking reply sit silent for up to
/// forty-five seconds. It is now step 3 of the <c>locate_a_place</c> flow, announced before it
/// starts and reported when it ends. The problem was never that it runs; it was that it ran
/// invisibly.
/// </para>
///
/// <para>
/// <b>Never independently offerable</b> (FR-060): it is a flow step, and offering step 3 on its
/// own would let a user pick something that cannot run without step 1.
/// </para>
/// </summary>
public sealed class ResolveSiteBoundaryCapability(IBoundaryResolutionService boundaryResolutionService) : IConversationCapability
{
    public const string CapabilityKey = "resolve_site_boundary";

    public string Name => CapabilityKey;

    public string Description =>
        "Finds the outline of the site around a confirmed location, draws it on the map, and reports its area and match confidence.";

    public string WhenToUse =>
        "Use when a location is already confirmed and the user asks to outline, highlight, show " +
        "the extent of, or measure the site — or has accepted an offer to do so.";

    public string ArgumentHint => "nothing; it uses the location already confirmed this turn";

    public string UsageGuidance =>
        "Report the area and the confidence level together — a boundary is a best match, not a " +
        "survey, and a user acting on it needs to know how sure it is. Name the source " +
        "(OpenStreetMap, imagery) when confidence is anything below high. Do not re-run this for " +
        "a site already outlined; say it is already shown instead. This step is expensive and " +
        "slow, so it must never be started without the user having asked for it or accepted it.";

    public string Label => "Highlight the site boundary";

    public string OfferDescription => "Outline the site's extent on the map.";

    public string AcknowledgementTemplate => "Now highlighting the boundary.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ExternalNetwork];

    public string InputSchemaJson =>
        """{"type":"object","required":["latitude","longitude","locationName"],"properties":{"latitude":{"type":"number"},"longitude":{"type":"number"},"locationName":{"type":"string"},"confidence":{"type":"number"}}}""";

    // "source" (SourceDetail — a descriptive string, e.g. "OpenStreetMap") is what the narration
    // guidance above reports; "sourceType" (the SiteBoundarySource enum) plus the remaining fields
    // exist only so StructuredPayloadExtractor can rebuild the full ConfirmedSiteBoundaryData for
    // the __SITE_BOUNDARY__ event (specs/045 Phase 6) — the deciding/narrating model never needs
    // them and this schema is Tier 3 (never shown to it) regardless.
    public string OutputSchemaJson =>
        """{"type":"object","properties":{"siteName":{"type":"string"},"areaSquareMeters":{"type":"number"},"confidenceLevel":{"type":"string"},"source":{"type":"string"},"centroidLatitude":{"type":"number"},"centroidLongitude":{"type":"number"},"confidence":{"type":"number"},"sourceType":{"type":"string"},"polygon":{"type":"array"},"alternativeCandidateNames":{"type":"array"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Extended;

    /// <summary>
    /// Needs a confirmed location, and produces nothing new when that site is already outlined —
    /// precondition and non-redundancy, the two rules that belong to the capability itself.
    /// </summary>
    public bool IsAvailable(TurnContext context) =>
        context.HasActiveLocation && !context.IsBoundaryCurrentForActiveLocation;

    /// <summary>Never offered on its own; reached through the flow (FR-060).</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var root = input.RootElement;
        if (!root.TryGetProperty("latitude", out var latEl) ||
            !root.TryGetProperty("longitude", out var lonEl) ||
            !root.TryGetProperty("locationName", out var nameEl) ||
            nameEl.GetString() is not { Length: > 0 } locationName)
        {
            return AgentToolResult.Failure("A confirmed location (latitude, longitude and name) is required.");
        }

        var confidence = root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 1d;
        var confirmedLocation = new ConfirmedLocationData(latEl.GetDouble(), lonEl.GetDouble(), locationName, confidence);

        try
        {
            var outcome = await boundaryResolutionService.ResolveAsync(
                confirmedLocation, context.UserChatId ?? Guid.Empty, cancellationToken);

            if (outcome.ConfirmedBoundary is not { } boundary)
            {
                return AgentToolResult.Failure(outcome.ConfirmationText ?? "No site boundary could be resolved.");
            }

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                siteName = boundary.SiteName,
                areaSquareMeters = boundary.AreaSquareMeters,
                confidenceLevel = boundary.ConfidenceLevel.ToString(),
                source = boundary.SourceDetail,
                centroidLatitude = boundary.CentroidLatitude,
                centroidLongitude = boundary.CentroidLongitude,
                confidence = boundary.Confidence,
                sourceType = boundary.Source.ToString(),
                polygon = boundary.Polygon.Select(p => new { latitude = p.Latitude, longitude = p.Longitude }),
                alternativeCandidateNames = boundary.AlternativeCandidateNames,
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AgentToolResult.Failure($"The boundary lookup failed: {ex.Message}");
        }
    }
}
