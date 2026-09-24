using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetVoiceEngines;

public sealed class GetVoiceEnginesQueryHandler(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines,
    IHostedModelLocator hostedModels,
    IAIProviderRepository aiProviders)
    : IRequestHandler<GetVoiceEnginesQuery, IReadOnlyList<VoiceEngineDto>>
{
    public async Task<IReadOnlyList<VoiceEngineDto>> Handle(GetVoiceEnginesQuery request, CancellationToken cancellationToken)
    {
        var rows = await voiceProviders.ListByPriorityAsync(cancellationToken);
        var addedKeys = rows.Select(r => r.ProviderKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var speechVendors = await VoiceEngineResolution.ListSpeechVendorsAsync(aiProviders, cancellationToken);
        var offered = new List<VoiceEngineDto>();
        foreach (var engine in engines.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            // specs/072 FR-036/FR-039: an on-server engine is left out only while its custom model is
            // Unavailable; with no completed record it keeps today's configured install.
            if (engine is IHostedModelEngine hosted
                && await hostedModels.ResolveAsync(hosted.ModelRepositoryId, cancellationToken) is HostedModelResolution.Unavailable)
            {
                continue;
            }

            offered.Add(new VoiceEngineDto(
                engine.ProviderKey,
                engine.DisplayName,
                engine.RequiresCredential,
                addedKeys.Contains(engine.ProviderKey),
                IsKeyedAsAiProvider: VoiceEngineResolution.FindVendor(speechVendors, engine.ProviderKey) is not null));
        }

        return offered;
    }
}
