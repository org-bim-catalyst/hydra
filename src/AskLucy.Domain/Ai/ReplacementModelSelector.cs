namespace AskLucy.Domain.Ai;

/// <summary>
/// specs/069 research D1: ranks same-provider Available models as replacements for a retired one.
/// Only candidates that support every capability the retired model has are returned.
/// </summary>
public static class ReplacementModelSelector
{
    public static IReadOnlyList<AIModel> Rank(
        AIModel retired,
        IEnumerable<AIModel> candidates,
        IReadOnlySet<Guid> excludedModelIds)
    {
        ArgumentNullException.ThrowIfNull(retired);

        var eligible = candidates
            .Where(c => c.ProviderId == retired.ProviderId)
            .Where(c => c.Status == AIModelStatus.Available)
            .Where(c => c.Id != retired.Id && !excludedModelIds.Contains(c.Id))
            .Where(c => MissingCapabilities(retired, c).Count == 0);

        return eligible
            .OrderBy(c => PriceBucket(retired, c))
            .ThenBy(c => PriceDistance(retired, c))
            .ThenBy(c => ContextBucket(retired, c))
            .ThenByDescending(c => c.ReleaseDate.HasValue ? c.ReleaseDate.Value.DayNumber : int.MinValue)
            .ThenBy(c => c.ModelKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The capability flags <paramref name="retired"/> has that <paramref name="candidate"/> lacks.</summary>
    public static IReadOnlyList<string> MissingCapabilities(AIModel retired, AIModel candidate)
    {
        var missing = new List<string>();
        Check(missing, retired.SupportsStreaming, candidate.SupportsStreaming, nameof(AIModel.SupportsStreaming));
        Check(missing, retired.SupportsVision, candidate.SupportsVision, nameof(AIModel.SupportsVision));
        Check(missing, retired.SupportsFunctionCalling, candidate.SupportsFunctionCalling, nameof(AIModel.SupportsFunctionCalling));
        Check(missing, retired.SupportsJsonMode, candidate.SupportsJsonMode, nameof(AIModel.SupportsJsonMode));
        Check(missing, retired.SupportsReasoning, candidate.SupportsReasoning, nameof(AIModel.SupportsReasoning));
        Check(missing, retired.SupportsEmbeddings, candidate.SupportsEmbeddings, nameof(AIModel.SupportsEmbeddings));
        Check(missing, retired.SupportsImageInput, candidate.SupportsImageInput, nameof(AIModel.SupportsImageInput));
        Check(missing, retired.SupportsImageOutput, candidate.SupportsImageOutput, nameof(AIModel.SupportsImageOutput));
        Check(missing, retired.SupportsAudio, candidate.SupportsAudio, nameof(AIModel.SupportsAudio));
        return missing;
    }

    private static void Check(List<string> missing, bool needed, bool offered, string flag)
    {
        if (needed && !offered)
        {
            missing.Add(flag);
        }
    }

    // A priced candidate ranks before an unpriced one. A retired model without a price ignores price.
    private static int PriceBucket(AIModel retired, AIModel candidate) =>
        retired.Pricing is null ? 0 : candidate.Pricing is null ? 1 : 0;

    private static decimal PriceDistance(AIModel retired, AIModel candidate)
    {
        if (retired.Pricing is null || candidate.Pricing is null)
        {
            return 0m;
        }

        return Math.Abs(candidate.Pricing.InputPerMillionTokensUsd - retired.Pricing.InputPerMillionTokensUsd)
            + Math.Abs(candidate.Pricing.OutputPerMillionTokensUsd - retired.Pricing.OutputPerMillionTokensUsd);
    }

    // A context window at least as large as the retired model's ranks first. An unknown window ranks last.
    private static int ContextBucket(AIModel retired, AIModel candidate)
    {
        if (retired.ContextWindowTokens is null)
        {
            return 0;
        }

        if (candidate.ContextWindowTokens is null)
        {
            return 2;
        }

        return candidate.ContextWindowTokens >= retired.ContextWindowTokens ? 0 : 1;
    }
}
