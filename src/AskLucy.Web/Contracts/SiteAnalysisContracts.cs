namespace AskLucy.Web.Contracts;

/// <summary>specs/050-park-site-analysis-agent contracts — `POST /api/v1/site-analysis/project-links`.</summary>
public sealed record CreateSiteAnalysisProjectLinkFromDeepLinkRequest(string TheDigitalCoreProjectId, string SiteName);

public sealed record CreateSiteAnalysisProjectLinkFromDeepLinkResponse(Guid UserChatId);
