using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using MediatR;

namespace AskLucy.Application.Ai.Commands.ApplyProviderModelSync;

/// <summary>
/// contracts/admin-ai-models.md `POST .../models/actions/sync/apply` — the client echoes
/// back exactly the rows it reviewed and selected (FR-007); no server-side cache of the
/// proposal. specs/009-selective-model-sync-review FR-007a: applied best-effort, per row —
/// see <see cref="ApplyProviderModelSyncResultDto"/>.
/// </summary>
public sealed record ApplyProviderModelSyncCommand(
    Guid ProviderId,
    IReadOnlyList<ProviderModelInfo> Added,
    IReadOnlyList<RemovedModelDto> RemovedFromVendor) : IRequest<ApplyProviderModelSyncResultDto>;
