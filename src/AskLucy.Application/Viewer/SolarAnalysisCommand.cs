namespace AskLucy.Application.Viewer;

/// <summary>
/// specs/052-solar-analysis contracts/open-solar-analysis-capability.md — carried on the final
/// <see cref="Ai.Commands.SendChatMessage.ChatStreamChunk"/> exactly as
/// <see cref="ViewerContentCommand"/> does (research D3: the same trailing-SSE mechanism, not a
/// new one). Deliberately carries no coordinates — the browser activates the analysis for the
/// viewer's own active site, never one this command could point at.
/// </summary>
public sealed record SolarAnalysisCommand(string Date, string TimeOfDay);
