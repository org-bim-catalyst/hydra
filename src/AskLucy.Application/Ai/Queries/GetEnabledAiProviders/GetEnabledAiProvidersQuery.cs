using MediatR;

namespace AskLucy.Application.Ai.Queries.GetEnabledAiProviders;

/// <summary>contracts/providers.md `GET /api/v1/ai/providers` — enabled providers only (FR-007).</summary>
public sealed record GetEnabledAiProvidersQuery : IRequest<IReadOnlyList<ProviderSummaryDto>>;
