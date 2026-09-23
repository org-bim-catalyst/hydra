using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAdminVoiceProviders;

public sealed class GetAdminVoiceProvidersQueryHandler(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines)
    : IRequestHandler<GetAdminVoiceProvidersQuery, IReadOnlyList<AdminVoiceProviderDto>>
{
    public async Task<IReadOnlyList<AdminVoiceProviderDto>> Handle(GetAdminVoiceProvidersQuery request, CancellationToken cancellationToken)
    {
        var rows = await voiceProviders.ListByPriorityAsync(cancellationToken);

        return [.. rows.Select((row, index) => AdminVoiceProviderDto.FromEntity(
            row,
            isPrimary: index == 0,
            requiresCredential: VoiceEngineResolution.FindEngine(engines, row.ProviderKey)?.RequiresCredential ?? false))];
    }
}
