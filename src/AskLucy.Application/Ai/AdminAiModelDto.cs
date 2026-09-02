using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>specs/008-ai-model-catalog-management FR-001 — the admin view of one model, any status. Adds `Status` to `ModelSummaryDto`'s shape, which deliberately omits it (end users only ever see `Available` models).</summary>
public sealed record AdminAiModelDto(
    Guid Id,
    string ModelKey,
    string DisplayName,
    int? ContextWindowTokens,
    int? MaxOutputTokens,
    AIModelCapabilities Capabilities,
    ModelPricing? Pricing,
    DateOnly? ReleaseDate,
    AIModelStatus Status)
{
    public static AdminAiModelDto FromEntity(AIModel model) => new(
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
        model.Status);
}
