namespace AskLucy.Domain.Ai;

/// <summary>Per-token pricing for an <see cref="AIModel"/> (FR-022). Null on the owning model means pricing is unknown — never fabricate a zero (data-model.md).</summary>
public sealed record ModelPricing(decimal InputPerMillionTokensUsd, decimal OutputPerMillionTokensUsd);
