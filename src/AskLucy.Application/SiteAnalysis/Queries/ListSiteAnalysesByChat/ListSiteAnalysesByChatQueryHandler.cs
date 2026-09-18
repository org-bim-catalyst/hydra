using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteAnalysis.Queries.GetSiteAnalysis;
using MediatR;

namespace AskLucy.Application.SiteAnalysis.Queries.ListSiteAnalysesByChat;

/// <summary>Scoped by (userId, userChatId) at the repository level (FR-019) — never a caller-supplied chat id alone.</summary>
public sealed class ListSiteAnalysesByChatQueryHandler(ISiteAnalysisRepository repository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListSiteAnalysesByChatQuery, IReadOnlyList<SiteAnalysisDetailDto>>
{
    public async Task<IReadOnlyList<SiteAnalysisDetailDto>> Handle(ListSiteAnalysesByChatQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var analyses = await repository.ListByChatAsync(userId, request.UserChatId, cancellationToken);
        return [.. analyses.Select(SiteAnalysisDetailDto.Create)];
    }
}
