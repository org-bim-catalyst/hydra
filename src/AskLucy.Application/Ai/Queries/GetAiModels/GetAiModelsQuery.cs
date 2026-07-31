using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAiModels;

/// <summary>
/// contracts/providers.md — `ProviderId` set backs `GET /api/v1/ai/providers/{id}/models`;
/// `ProviderId` null backs the flat cross-provider `GET /api/v1/ai/models`.
/// </summary>
public sealed record GetAiModelsQuery(Guid? ProviderId) : IRequest<IReadOnlyList<ModelSummaryDto>>;
