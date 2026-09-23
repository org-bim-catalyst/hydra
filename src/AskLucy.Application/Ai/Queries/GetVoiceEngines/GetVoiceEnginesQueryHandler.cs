using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetVoiceEngines;

public sealed class GetVoiceEnginesQueryHandler(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines)
    : IRequestHandler<GetVoiceEnginesQuery, IReadOnlyList<VoiceEngineDto>>
{
    public async Task<IReadOnlyList<VoiceEngineDto>> Handle(GetVoiceEnginesQuery request, CancellationToken cancellationToken)
    {
        var rows = await voiceProviders.ListByPriorityAsync(cancellationToken);
        var addedKeys = rows.Select(r => r.ProviderKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. engines
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(e => new VoiceEngineDto(e.ProviderKey, e.DisplayName, e.RequiresCredential, addedKeys.Contains(e.ProviderKey)))];
    }
}
