using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetPromptStatistics;

/// <summary>spec.md "Prompt Statistics" API requirement, FR-062, contracts/prompts-api.md `GET /api/v1/prompts/{id}/statistics`.</summary>
public sealed record GetPromptStatisticsQuery(Guid PromptId) : IRequest<PromptStatisticsDto>;
