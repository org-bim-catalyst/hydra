using System.Text.Json;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Application.Viewer;
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

                // specs/077 — choosing which buildings the site includes redraws it exactly as
                // resolving it did, so both results share SiteBoundaryPayload's one shape.
                case ResolveSiteBoundaryCapability.CapabilityKey or SetSiteBoundaryMembersCapability.CapabilityKey
                    when root.TryGetProperty("siteName", out _) && root.TryGetProperty("polygon", out _):
                    return new ChatStreamChunk(null, null, ConfirmedBoundary: SiteBoundaryPayload.Read(root));

                case AdjustViewerFocusCapability.CapabilityKey
                    when root.TryGetProperty("direction", out var directionEl):
                    return new ChatStreamChunk(null, null,
                        ViewerZoom: new ViewerZoomCommand(directionEl.GetString() ?? "in"));

                case LoadViewerContentCapability.CapabilityKey
                    when root.TryGetProperty("fileId", out var fileIdEl):
                    return new ChatStreamChunk(null, null, ViewerContent: new ViewerContentCommand(
                        fileIdEl.GetString() ?? string.Empty,
                        root.GetProperty("latitude").GetDouble(),
                        root.GetProperty("longitude").GetDouble(),
                        root.TryGetProperty("heightMetres", out var heightEl) ? heightEl.GetDouble() : 0d,
                        root.TryGetProperty("orientationDegrees", out var orientationEl) ? orientationEl.GetDouble() : 0d,
                        root.TryGetProperty("scale", out var scaleEl) ? scaleEl.GetDouble() : 1d));

                // specs/052-solar-analysis research D3 — only the "opened" success case produces a
                // client-visible command; a refusal (opened: false) carries nothing for the viewer
                // to act on, so it is deliberately NOT matched here (falls through to `default`)
                // and Lucy narrates the refusal from the tool result text alone.
                case OpenSolarAnalysisCapability.CapabilityKey
                    when root.TryGetProperty("opened", out var openedEl) && openedEl.GetBoolean():
                    return new ChatStreamChunk(null, null, SolarAnalysis: new SolarAnalysisCommand(
                        root.TryGetProperty("date", out var solarDateEl) ? solarDateEl.GetString() ?? string.Empty : string.Empty,
                        root.TryGetProperty("timeOfDay", out var solarTimeEl) ? solarTimeEl.GetString() ?? string.Empty : string.Empty));

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
        catch (InvalidOperationException)
        {
            // A field of the wrong JSON kind (e.g. a string where a number belongs).
            return null;
        }
    }
}
