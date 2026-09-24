using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAdminVoiceProviders;

public sealed class GetAdminVoiceProvidersQueryHandler(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines,
    IAIProviderRepository aiProviders)
    : IRequestHandler<GetAdminVoiceProvidersQuery, IReadOnlyList<AdminVoiceProviderDto>>
{
    public async Task<IReadOnlyList<AdminVoiceProviderDto>> Handle(GetAdminVoiceProvidersQuery request, CancellationToken cancellationToken)
    {
        var rows = await voiceProviders.ListByPriorityAsync(cancellationToken);

        var speechVendors = await VoiceEngineResolution.ListSpeechVendorsAsync(aiProviders, cancellationToken);
        return await VoiceEngineResolution.ToDtosAsync(engines, rows, speechVendors, cancellationToken);
    }
}
