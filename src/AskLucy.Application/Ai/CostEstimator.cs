using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>Computes an estimated USD cost from a model's <see cref="ModelPricing"/> and observed token counts. Returns null — never a fabricated zero — when pricing is missing (FR-022).</summary>
public static class CostEstimator
{
    public static decimal? Estimate(ModelPricing? pricing, int? inputTokenCount, int? outputTokenCount)
    {
        if (pricing is null || inputTokenCount is null || outputTokenCount is null)
        {
            return null;
        }

        var inputCost = inputTokenCount.Value / 1_000_000m * pricing.InputPerMillionTokensUsd;
        var outputCost = outputTokenCount.Value / 1_000_000m * pricing.OutputPerMillionTokensUsd;
        return inputCost + outputCost;
    }
}
