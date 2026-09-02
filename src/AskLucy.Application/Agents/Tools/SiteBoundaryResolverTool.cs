using System.Text.Json;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Tools;

/// <summary>
/// specs/042-site-boundary-resolution research.md #11, contracts/site-boundary-resolver-tool.md —
/// **secondary** surface for a user-authored custom AI Agent (spec 020) that wants to call
/// boundary resolution explicitly, outside a normal Lucy chat turn. The primary mechanism for
/// the base chat experience is the <c>SendChatMessageCommandHandler</c> pipeline hook, not this
/// tool — both are thin callers of the same <see cref="IBoundaryResolutionService"/>, so no
/// scoring/candidate-search logic is duplicated between them.
///
/// Uses <see cref="IGeocodingProvider"/> directly (top candidate by importance) rather than
/// <see cref="ILocationResolutionService"/> — that service's intent-classification prompt is
/// tuned for a full conversational message ("show me X"), not a bare place-name tool argument,
/// so reusing it here would risk misclassifying a plain query like "Al Safa Park 2" as having no
/// location intent at all.
/// </summary>
public sealed class SiteBoundaryResolverTool(
    IGeocodingProvider geocodingProvider,
    IBoundaryResolutionService boundaryResolutionService) : IAgentTool
{
    public string Name => "SiteBoundaryResolverTool";

    public string Description =>
        "Resolves a named or addressed site's geographic boundary as a polygon, with a confidence level and data source.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ExternalNetwork];

    public string InputSchemaJson =>
        """{"type":"object","required":["locationQuery"],"properties":{"locationQuery":{"type":"string"},"radiusMeters":{"type":"integer","minimum":50,"maximum":5000}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","required":["outcome","message"],"properties":{"outcome":{"type":"string","enum":["confirmed","no_candidates","not_found","unavailable"]},"message":{"type":"string"},"boundary":{"type":"object"}}}""";

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("locationQuery", out var queryElement) || queryElement.GetString() is not { Length: > 0 } locationQuery)
        {
            return AgentToolResult.Failure("A non-empty locationQuery is required.");
        }

        IReadOnlyList<GeocodingCandidate> candidates;
        try
        {
            candidates = await geocodingProvider.SearchAsync(locationQuery, cancellationToken);
        }
        catch (GeocodingProviderUnavailableException)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                outcome = "unavailable",
                message = "Location lookup is unavailable right now — please try again in a moment.",
            }));
        }

        var winner = candidates.OrderByDescending(c => c.Importance).FirstOrDefault();
        if (winner is null)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                outcome = "not_found",
                message = $"Could not find a location matching '{locationQuery}'.",
            }));
        }

        var confirmedLocation = new ConfirmedLocationData(winner.Latitude, winner.Longitude, winner.LocationName, winner.Importance);
        var boundaryOutcome = await boundaryResolutionService.ResolveAsync(confirmedLocation, context.UserChatId ?? Guid.Empty, cancellationToken);

        if (boundaryOutcome.Type == BoundaryResolutionOutcomeType.Unavailable || boundaryOutcome.ConfirmedBoundary is null)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                outcome = "unavailable",
                message = boundaryOutcome.ConfirmationText,
            }));
        }

        var boundary = boundaryOutcome.ConfirmedBoundary;
        var outcomeName = boundaryOutcome.Type == BoundaryResolutionOutcomeType.Confirmed ? "confirmed" : "no_candidates";

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
        {
            outcome = outcomeName,
            message = boundaryOutcome.ConfirmationText,
            boundary = new
            {
                siteName = boundary.SiteName,
                centroid = new { latitude = boundary.CentroidLatitude, longitude = boundary.CentroidLongitude },
                polygon = boundary.Polygon.Select(p => new { latitude = p.Latitude, longitude = p.Longitude }),
                areaSquareMeters = boundary.AreaSquareMeters,
                confidence = boundary.Confidence,
                confidenceLevel = boundary.ConfidenceLevel.ToString().ToLowerInvariant(),
                source = boundary.Source.ToString().ToLowerInvariant() switch
                {
                    "osmboundary" => "osm-boundary",
                    "governmentcadastral" => "government-cadastral",
                    "aiinterpretation" => "ai-interpretation",
                    "uploadedboundary" => "uploaded-boundary",
                    "manualfallback" => "manual-fallback",
                    var other => other,
                },
                sourceDetail = boundary.SourceDetail,
                notes = boundaryOutcome.ConfirmationText is null ? Array.Empty<string>() : new[] { boundaryOutcome.ConfirmationText },
                alternativeCandidateNames = boundary.AlternativeCandidateNames,
            },
        }));
    }
}
