using AskLucy.Application.Abstractions;
using AskLucy.Domain.SiteAnalysis;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class SiteAnalysisProjectLinkRepository(AskLucyDbContext dbContext) : ISiteAnalysisProjectLinkRepository
{
    public Task<SiteAnalysisProjectLink?> GetByUserChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default) =>
        dbContext.SiteAnalysisProjectLinks.FirstOrDefaultAsync(l => l.UserChatId == userChatId, cancellationToken);

    public Task<SiteAnalysisProjectLink?> GetByTheDigitalCoreProjectIdAsync(string theDigitalCoreProjectId, CancellationToken cancellationToken = default) =>
        dbContext.SiteAnalysisProjectLinks.FirstOrDefaultAsync(l => l.TheDigitalCoreProjectId == theDigitalCoreProjectId, cancellationToken);

    public void Add(SiteAnalysisProjectLink link) => dbContext.SiteAnalysisProjectLinks.Add(link);
}
