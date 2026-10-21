namespace AskLucy.Application.Viewer;

/// <summary>
/// specs/083-map-snapshot — the captured aerial image carried on the final
/// <see cref="Ai.Commands.SendChatMessage.ChatStreamChunk"/> exactly as
/// <see cref="SolarAnalysisCommand"/> does (research D3: the same trailing-SSE mechanism, not a
/// new one). Carries the platform Document id rather than the bytes: the picture has to survive a
/// reload, and the client resolves that id to a fresh signed URL every time it renders — the
/// same route a generated image already takes.
/// </summary>
public sealed record MapSnapshotCommand(string DocumentId, string LocationName);
