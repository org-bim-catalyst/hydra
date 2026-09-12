namespace AskLucy.Application.Viewer;

/// <summary>
/// specs/051-viewer-scene-content-api FR-004 — content Lucy asked the viewer to load, carried on
/// the final <see cref="Ai.Commands.SendChatMessage.ChatStreamChunk"/> exactly as
/// <see cref="Locations.ViewerZoomCommand"/> does (research D8: the same capability-dispatch
/// mechanism, not a new one). The frontend performs the actual
/// <c>engine.loadContent({ kind: 'model', format: 'gltf', fileId }, placement)</c> call — this
/// record only carries what to load and where, never a content id (the engine assigns its own).
/// </summary>
public sealed record ViewerContentCommand(
    string FileId,
    double Latitude,
    double Longitude,
    double HeightMetres,
    double OrientationDegrees,
    double Scale);
