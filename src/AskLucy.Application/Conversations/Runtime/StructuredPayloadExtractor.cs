using System.Text.Json;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// Recovers the frozen viewer payloads (FR-048) from a capability's own JSON output. Kept as one
/// small adapter rather than having each capability emit these directly: capabilities speak
/// <see cref="Agents.Tools.AgentToolResult"/> JSON so they stay usable by the background agent
/// runtime too, and only the conversational turn needs to know these specific shapes exist.
/// <para>
/// Shared between <see cref="ConversationTurnOrchestrator"/> (a standalone capability slice) and
/// <see cref="Flows.FlowRunner"/> (a flow step) — extracted here (specs/045 Phase 6) so the two
/// call sites read one capability's result the same way rather than keeping two copies in sync.
/// </para>
/// </summary>
public static class StructuredPayloadExtractor
{
    public static ChatStreamChunk? TryExtract(string capabilityKey, string resultJson)
    {
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            var root = document.RootElement;

            switch (capabilityKey)
            {
                case ResolveLocationCapability.CapabilityKey
                    when root.TryGetProperty("locationName", out var nameEl):
                    return new ChatStreamChunk(null, null, ConfirmedLocation: new ConfirmedLocationData(
                        root.GetProperty("latitude").GetDouble(),
                        root.GetProperty("longitude").GetDouble(),
                        nameEl.GetString() ?? string.Empty,
                        root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 1d,
                        LocationType: root.TryGetProperty("locationType", out var locationTypeEl) ? locationTypeEl.GetString() : null,
                        Viewport: root.TryGetProperty("viewport", out var viewportEl) && viewportEl.ValueKind != JsonValueKind.Null
                            ? new ViewportBounds(
                                viewportEl.GetProperty("northeastLat").GetDouble(),
                                viewportEl.GetProperty("northeastLng").GetDouble(),
                                viewportEl.GetProperty("southwestLat").GetDouble(),
                                viewportEl.GetProperty("southwestLng").GetDouble())
                            : null));

                case ResolveSiteBoundaryCapability.CapabilityKey
                    when root.TryGetProperty("siteName", out var siteNameEl) && root.TryGetProperty("polygon", out var polygonEl):
                    return new ChatStreamChunk(null, null, ConfirmedBoundary: new ConfirmedSiteBoundaryData(
                        siteNameEl.GetString() ?? string.Empty,
                        root.GetProperty("centroidLatitude").GetDouble(),
                        root.GetProperty("centroidLongitude").GetDouble(),
                        [.. polygonEl.EnumerateArray().Select(p => new GeoPoint(
                            p.GetProperty("latitude").GetDouble(), p.GetProperty("longitude").GetDouble()))],
                        root.GetProperty("areaSquareMeters").GetDouble(),
                        root.TryGetProperty("confidence", out var boundaryConfEl) ? boundaryConfEl.GetDouble() : 1d,
                        Enum.Parse<BoundaryConfidenceLevel>(root.GetProperty("confidenceLevel").GetString()!),
                        Enum.Parse<SiteBoundarySource>(root.GetProperty("sourceType").GetString()!),
                        root.TryGetProperty("source", out var sourceDetailEl) ? sourceDetailEl.GetString() ?? string.Empty : string.Empty,
                        root.TryGetProperty("alternativeCandidateNames", out var altEl)
                            ? [.. altEl.EnumerateArray().Select(a => a.GetString() ?? string.Empty)]
                            : []));

                case AdjustViewerFocusCapability.CapabilityKey
                    when root.TryGetProperty("direction", out var directionEl):
                    return new ChatStreamChunk(null, null,
                        ViewerZoom: new ViewerZoomCommand(directionEl.GetString() ?? "in"));

                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            // A result missing a field this switch's own capability branch requires — treated the
            // same as unparseable rather than throwing: the narration already told the user what
            // happened, and a missing viewer payload is a degradation, not a turn failure.
            return null;
        }
        catch (ArgumentException)
        {
            // Enum.Parse on a value that doesn't match BoundaryConfidenceLevel/SiteBoundarySource.
            return null;
        }
    }
}
