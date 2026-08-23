using MediatR;

namespace AskLucy.Application.SiteAnalysis.Commands.CreateSiteAnalysisProjectLinkFromDeepLink;

/// <summary>FR-024(a): a user follows a Project-linked deep link from TheDigitalCore. Reuses an
/// already-linked conversation for this Project if one exists (idempotent for repeat visits),
/// otherwise creates a new one.</summary>
public sealed record CreateSiteAnalysisProjectLinkFromDeepLinkCommand(
    string TheDigitalCoreProjectId, string SiteName) : IRequest<CreateSiteAnalysisProjectLinkFromDeepLinkResult>;

public sealed record CreateSiteAnalysisProjectLinkFromDeepLinkResult(Guid UserChatId);
