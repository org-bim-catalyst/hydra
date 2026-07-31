using MediatR;

namespace AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;

/// <summary>contracts/admin-ai-models.md `POST /api/v1/admin/ai/providers/{providerId}/models/actions/sync` — a Query, not a Command, despite the verb: it never mutates the catalog (FR-006, research.md Decision 3).</summary>
public sealed record GetProviderModelSyncDiffQuery(Guid ProviderId) : IRequest<ProviderModelSyncDiffDto>;
