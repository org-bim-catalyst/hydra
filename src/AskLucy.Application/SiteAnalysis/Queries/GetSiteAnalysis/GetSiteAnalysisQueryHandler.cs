using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.SiteAnalysis.Queries.GetSiteAnalysis;

/// <summary>contracts/site-analysis-api.md — ownership enforced here (FR-019); an analysis belonging to another user is reported as a plain 404, never a 403 that would disclose existence.</summary>
public sealed class GetSiteAnalysisQueryHandler(
    ISiteAnalysisRepository repository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetSiteAnalysisQuery, SiteAnalysisDetailDto>
{
    public async Task<SiteAnalysisDetailDto> Handle(GetSiteAnalysisQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var analysis = await repository.GetByIdForUserAsync(request.Id, userId, cancellationToken)
            ?? throw new KeyNotFoundException("Site analysis not found.");

        return SiteAnalysisDetailDto.Create(analysis);
    }
}
