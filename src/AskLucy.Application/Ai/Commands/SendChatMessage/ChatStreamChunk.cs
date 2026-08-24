using AskLucy.Application.Abstractions;
using AskLucy.Application.Locations;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// specs/036-startup-geolocation US3: agent-confirmed location data carried on the final
/// <see cref="ChatStreamChunk"/> so the controller can emit the distinguishable
/// <c>__LOCATION__</c> SSE trailing event without any other chunk needing to know about it.
/// specs/038-viewer-poi-zoom: extended with <see cref="LocationType"/> and
/// <see cref="Viewport"/> so the frontend can zoom to the correct altitude.
/// </summary>
public sealed record ConfirmedLocationData(
    double Latitude,
    double Longitude,
    string LocationName,
    double Confidence,
    string Source = "agent",
    string? LocationType = null,
    ViewportBounds? Viewport = null);

/// <summary>
/// <see cref="SendChatMessageCommand"/>'s own stream element — wraps the shared
/// <see cref="StreamChunk"/> (content delta + optional usage, same as every other
/// <see cref="IAIProvider"/> consumer) plus an optional <see cref="RagRetrievalOutcome"/>
/// carried on the final chunk only, mirroring how <see cref="ChatUsage"/> already rides the
/// final chunk(s) rather than every one. Kept separate from <see cref="StreamChunk"/> itself
/// (rather than adding these fields there) so RAG stays a concern of this one command, not of
/// every <see cref="IAIProvider"/> implementation (OpenAI/Anthropic/Gemini/OpenRouter never
/// need to know about retrieval). <see cref="MemoryOutcome"/> (specs/018-ai-memory-system) and
/// <see cref="ConfirmedLocation"/> (specs/036-startup-geolocation) ride the final chunk the
/// same way, for the same reason. <see cref="ViewerZoom"/> (specs/038-viewer-poi-zoom) carries
/// an explicit zoom command when detected in the user's message.
/// </summary>
public sealed record ChatStreamChunk(
    string? ContentDelta,
    ChatUsage? Usage,
    RagRetrievalOutcome? RetrievalOutcome = null,
    MemoryRetrievalOutcome? MemoryOutcome = null,
    ConfirmedLocationData? ConfirmedLocation = null,
    ViewerZoomCommand? ViewerZoom = null);
