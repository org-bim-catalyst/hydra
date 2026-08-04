using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBaseDashboardSummary;

public sealed record GetKnowledgeBaseDashboardSummaryQuery : IRequest<KnowledgeBaseDashboardSummaryDto>;
