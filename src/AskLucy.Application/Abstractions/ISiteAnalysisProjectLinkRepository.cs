using AskLucy.Domain.SiteAnalysis;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="SiteAnalysisProjectLink"/> (data-model.md).</summary>
public interface ISiteAnalysisProjectLinkRepository
{
    Task<SiteAnalysisProjectLink?> GetByUserChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default);

    Task<SiteAnalysisProjectLink?> GetByTheDigitalCoreProjectIdAsync(string theDigitalCoreProjectId, CancellationToken cancellationToken = default);

    void Add(SiteAnalysisProjectLink link);
}
