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
    IReadOnlyDictionary<Guid, ReplacementChoice>? Replacements = null) : IRequest<ApplyProviderModelSyncResultDto>
{
    /// <summary>specs/069 FR-007: the choices the client echoed back on each removed row.</summary>
    public static IReadOnlyDictionary<Guid, ReplacementChoice> ChoicesFrom(IReadOnlyList<RemovedModelDto> removed) =>
        removed
            .Where(r => r.Replacement is not null)
            .ToDictionary(
                r => r.Id,
                r => r.Replacement!.Kind switch
                {
                    "Model" when r.Replacement.ModelId is { } modelId => new ReplacementChoice(ReplacementChoiceKind.Model, modelId),
                    "None" => new ReplacementChoice(ReplacementChoiceKind.None),
                    _ => ReplacementChoice.Proposed,
                });
}
