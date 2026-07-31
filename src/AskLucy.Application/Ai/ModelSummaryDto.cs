using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>contracts/providers.md's `ModelSummaryDto` — `Available` models only. `ProviderId`/`ProviderDisplayName` are always populated (a small simplification over the contract's "only on the flat endpoint" note) so both endpoints share one shape.</summary>
public sealed record ModelSummaryDto(
    Guid Id,
    string ModelKey,
    string DisplayName,
    int ContextWindowTokens,
    int MaxOutputTokens,
    AIModelCapabilities Capabilities,
    ModelPricing? Pricing,
    DateOnly? ReleaseDate,
    Guid ProviderId,
    string ProviderDisplayName)
{
    public static ModelSummaryDto FromEntity(AIModel model, AIProvider provider) => new(
        model.Id,
        model.ModelKey,
        model.DisplayName,
        model.ContextWindowTokens,
        model.MaxOutputTokens,
        new AIModelCapabilities(
            model.SupportsStreaming, model.SupportsVision, model.SupportsFunctionCalling, model.SupportsJsonMode,
            model.SupportsReasoning, model.SupportsEmbeddings, model.SupportsImageInput, model.SupportsImageOutput, model.SupportsAudio),
        model.Pricing,
        model.ReleaseDate,
        provider.Id,
        provider.DisplayName);
}
