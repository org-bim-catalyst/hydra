using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

public enum AIModelStatus
{
    Available,
    Deprecated,
    Unavailable,
}

/// <summary>Capability flags supplied to <see cref="AIModel.Create"/> — grouped to keep the factory signature readable (FR-005).</summary>
public sealed record AIModelCapabilities(
    bool Streaming,
    bool Vision,
    bool FunctionCalling,
    bool JsonMode,
    bool Reasoning,
    bool Embeddings,
    bool ImageInput,
    bool ImageOutput,
    bool Audio);

/// <summary>
/// One selectable model offered by an <see cref="AIProvider"/> (FR-005/FR-006). Administrator-
/// curated (research.md Decision 5) — status transitions per Clarifications Session
/// 2026-07-30 Q2: Deprecated and Unavailable are both non-selectable (see FR-007) and differ
/// only in administrative meaning.
/// </summary>
public sealed class AIModel : BaseEntity
{
    public Guid ProviderId { get; private set; }

    public string ModelKey { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public int ContextWindowTokens { get; private set; }

    public int MaxOutputTokens { get; private set; }

    public bool SupportsStreaming { get; private set; }

    public bool SupportsVision { get; private set; }

    public bool SupportsFunctionCalling { get; private set; }

    public bool SupportsJsonMode { get; private set; }

    public bool SupportsReasoning { get; private set; }

    public bool SupportsEmbeddings { get; private set; }

    public bool SupportsImageInput { get; private set; }

    public bool SupportsImageOutput { get; private set; }

    public bool SupportsAudio { get; private set; }

    public AIModelStatus Status { get; private set; } = AIModelStatus.Available;

    public DateOnly? ReleaseDate { get; private set; }

    /// <summary>Null = pricing unknown (FR-022) — never a fabricated zero.</summary>
    public ModelPricing? Pricing { get; private set; }

    private AIModel()
    {
        // Required by EF Core materialization.
    }

    public static AIModel Create(
        Guid providerId,
        string modelKey,
        string displayName,
        int contextWindowTokens,
        int maxOutputTokens,
        AIModelCapabilities capabilities,
        DateOnly? releaseDate,
        ModelPricing? pricing,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            throw new DomainRuleViolationException("A model key is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainRuleViolationException("A display name is required.");
        }

        if (contextWindowTokens <= 0)
        {
            throw new DomainRuleViolationException("Context window must be greater than zero.");
        }

        if (maxOutputTokens <= 0)
        {
            throw new DomainRuleViolationException("Max output tokens must be greater than zero.");
        }

        return new AIModel
        {
            Id = Guid.CreateVersion7(),
            ProviderId = providerId,
            ModelKey = modelKey.Trim(),
            DisplayName = displayName.Trim(),
            ContextWindowTokens = contextWindowTokens,
            MaxOutputTokens = maxOutputTokens,
            SupportsStreaming = capabilities.Streaming,
            SupportsVision = capabilities.Vision,
            SupportsFunctionCalling = capabilities.FunctionCalling,
            SupportsJsonMode = capabilities.JsonMode,
            SupportsReasoning = capabilities.Reasoning,
            SupportsEmbeddings = capabilities.Embeddings,
            SupportsImageInput = capabilities.ImageInput,
            SupportsImageOutput = capabilities.ImageOutput,
            SupportsAudio = capabilities.Audio,
            Status = AIModelStatus.Available,
            ReleaseDate = releaseDate,
            Pricing = pricing,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>FR-006. Any transition is allowed — deprecating/disabling a model already in use is exactly the scenario FR-011 exists to handle safely.</summary>
    public void SetStatus(AIModelStatus status, string actor)
    {
        Status = status;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SetPricing(ModelPricing? pricing, string actor)
    {
        Pricing = pricing;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-007: only "Available" models may be selected, for new or ongoing conversations alike.</summary>
    public bool IsSelectable => Status == AIModelStatus.Available;
}
