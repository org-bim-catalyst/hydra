using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetVoiceProviderVoices;

public sealed class GetVoiceProviderVoicesQueryHandler(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines,
    IAiCredentialProtector credentialProtector,
    IAIProviderRepository aiProviders)
    : IRequestHandler<GetVoiceProviderVoicesQuery, IReadOnlyList<VoiceOptionDto>>
{
    public async Task<IReadOnlyList<VoiceOptionDto>> Handle(GetVoiceProviderVoicesQuery request, CancellationToken cancellationToken)
    {
        var provider = await voiceProviders.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Voice provider not found.");

        var engine = VoiceEngineResolution.GetEngine(engines, provider);
        var vendor = await VoiceEngineResolution.FindVendorAsync(aiProviders, provider.ProviderKey, cancellationToken);
        var apiKey = VoiceEngineResolution.ResolveApiKey(credentialProtector, provider, vendor);

        var voices = await engine.ListVoicesAsync(apiKey, cancellationToken);
        return [.. voices.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
