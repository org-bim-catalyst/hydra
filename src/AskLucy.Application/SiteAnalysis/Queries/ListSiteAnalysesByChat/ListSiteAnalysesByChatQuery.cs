using AskLucy.Application.SiteAnalysis.Queries.GetSiteAnalysis;
using MediatR;

namespace AskLucy.Application.SiteAnalysis.Queries.ListSiteAnalysesByChat;

/// <summary>tasks.md T047 — rehydration for a whole conversation on open. Bounded by construction: at most a handful of analyses per chat, never an unbounded list (constitution §6).</summary>
public sealed record ListSiteAnalysesByChatQuery(Guid UserChatId) : IRequest<IReadOnlyList<SiteAnalysisDetailDto>>;
