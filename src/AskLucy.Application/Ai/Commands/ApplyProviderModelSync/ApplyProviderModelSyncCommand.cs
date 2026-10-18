using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using MediatR;

namespace AskLucy.Application.Ai.Commands.ApplyProviderModelSync;

/// <summary>
/// contracts/admin-ai-models.md `POST .../models/actions/sync/apply` — the client echoes
/// back exactly the rows it reviewed and selected (FR-007); no server-side cache of the
/// proposal. specs/009-selective-model-sync-review FR-007a: applied best-effort, per row —
/// see <see cref="ApplyProviderModelSyncResultDto"/>.
/// <para>
/// specs/069: a removed model is deprecated, not made Unavailable. <see cref="Replacements"/>
/// holds the administrator's replacement choice per removed model id; a model with no entry
/// takes the system's proposed replacement.
/// </para>
/// </summary>
public sealed record ApplyProviderModelSyncCommand(
    Guid ProviderId,
    IReadOnlyList<ProviderModelInfo> Added,
    IReadOnlyList<RemovedModelDto> RemovedFromVendor,
    IReadOnlyDictionary<Guid, ReplacementChoice>? Replacements = null) : IRequest<ApplyProviderModelSyncResultDto>;
