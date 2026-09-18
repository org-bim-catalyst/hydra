using AskLucy.Application.Abstractions;
using AskLucy.Domain.SiteAnalysis;
using Microsoft.EntityFrameworkCore;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Persistence.Repositories;

public sealed class SiteAnalysisRepository(AskLucyDbContext dbContext) : ISiteAnalysisRepository
{
    public void Add(SiteAnalysisAggregate analysis) => dbContext.SiteAnalyses.Add(analysis);

    public Task<SiteAnalysisAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.SiteAnalyses.Include(a => a.Results).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<SiteAnalysisAggregate?> GetByIdForUserAsync(Guid id, string userId, CancellationToken cancellationToken = default) =>
        dbContext.SiteAnalyses.Include(a => a.Results).FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<SiteAnalysisAggregate>> ListByChatAsync(string userId, Guid userChatId, CancellationToken cancellationToken = default) =>
        await dbContext.SiteAnalyses
            .Include(a => a.Results)
            .Where(a => a.UserId == userId && a.UserChatId == userChatId)
            .OrderByDescending(a => a.StartedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<SiteAnalysisAggregate?> FindRunningForSiteAsync(string userId, Guid userChatId, string siteName, CancellationToken cancellationToken = default) =>
        dbContext.SiteAnalyses.FirstOrDefaultAsync(
            a => a.UserId == userId && a.UserChatId == userChatId && a.SiteName == siteName && a.Status == SiteAnalysisStatus.Running,
            cancellationToken);

    public Task RecordWorkflowExecutionIdAsync(Guid siteAnalysisId, Guid workflowExecutionId, CancellationToken cancellationToken = default) =>
        dbContext.SiteAnalyses
            .Where(a => a.Id == siteAnalysisId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.WorkflowExecutionId, workflowExecutionId), cancellationToken);
}
