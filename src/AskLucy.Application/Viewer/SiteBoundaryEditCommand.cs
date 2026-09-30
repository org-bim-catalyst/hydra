namespace AskLucy.Application.Viewer;

/// <summary>
/// specs/079 contracts/site-boundary-edit-sse-event.md - Lucy opening the outline editor for a chat's
/// site, carried on the final <see cref="Ai.Commands.SendChatMessage.ChatStreamChunk"/> exactly as
/// <see cref="SolarAnalysisCommand"/> is. Carries the revision the editor must start from, so a
/// client holding an older outline refetches first instead of editing a stale shape.
/// </summary>
public sealed record SiteBoundaryEditCommand(Guid ChatId, Guid Revision);
