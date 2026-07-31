using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAdminAiModels;

/// <summary>contracts/admin-ai-models.md `GET /api/v1/admin/ai/providers/{providerId}/models` — every model for a provider, any status (FR-001).</summary>
public sealed record GetAdminAiModelsQuery(Guid ProviderId) : IRequest<IReadOnlyList<AdminAiModelDto>>;
