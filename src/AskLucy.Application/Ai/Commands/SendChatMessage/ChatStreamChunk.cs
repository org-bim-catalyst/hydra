using AskLucy.Application.Abstractions;
using AskLucy.Application.Locations;
using AskLucy.Domain.SiteBoundaries;

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
/// <para>
/// <see cref="StartsNewMessage"/> asks the controller to close the assistant message built so
/// far and begin another. The boundary confirmation uses it: it is reporting a second action,
/// finished seconds after the first and often after the reply has been read, so appending it to
/// the same bubble ran two unrelated statements together — "…centred the viewer on it.I've
/// outlined…" — and silently rewrote a message the user had already seen.
/// </para>
/// </summary>
public sealed record ChatStreamChunk(
    string? ContentDelta,
    ChatUsage? Usage,
    RagRetrievalOutcome? RetrievalOutcome = null,
    MemoryRetrievalOutcome? MemoryOutcome = null,
    ConfirmedLocationData? ConfirmedLocation = null,
    ViewerZoomCommand? ViewerZoom = null,
    ConfirmedSiteBoundaryData? ConfirmedBoundary = null,
    bool StartsNewMessage = false,

    /// <summary>
    /// What the newly-opened message is waiting for, shown to the user while it is empty.
    /// </summary>
    /// <remarks>
    /// Set on a <see cref="StartsNewMessage"/> chunk that carries no text yet, so the break can be
    /// announced <i>before</i> the slow work rather than after it. Without this the reply sat
    /// on screen looking finished while the boundary resolved — up to 45 s of silence with
    /// nothing to say anything was still happening, and the reply was not spoken until it was
    /// over.
    /// </remarks>
    string? PendingLabel = null);

/// <summary>
/// specs/042-site-boundary-resolution — the resolved site boundary carried on the final
/// <see cref="ChatStreamChunk"/>, mirroring <see cref="ConfirmedLocationData"/> exactly.
/// </summary>
public sealed record ConfirmedSiteBoundaryData(
    string SiteName,
    double CentroidLatitude,
    double CentroidLongitude,
    IReadOnlyList<GeoPoint> Polygon,
    double AreaSquareMeters,
    double Confidence,
    BoundaryConfidenceLevel ConfidenceLevel,
    SiteBoundarySource Source,
    string SourceDetail,
    IReadOnlyList<string> AlternativeCandidateNames);
