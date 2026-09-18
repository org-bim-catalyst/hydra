using MediatR;

namespace AskLucy.Application.SiteAnalysis.Queries.GetSiteAnalysis;

/// <summary>contracts/site-analysis-api.md — the rehydration path (FR-017).</summary>
public sealed record GetSiteAnalysisQuery(Guid Id) : IRequest<SiteAnalysisDetailDto>;
