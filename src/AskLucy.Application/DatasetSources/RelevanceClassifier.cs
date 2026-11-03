using AskLucy.Application.Abstractions;
using AskLucy.Domain.DatasetSources;

namespace AskLucy.Application.DatasetSources;

/// <summary>
/// Classifies data sources into relevance categories based on domain analysis,
/// content keywords, and metadata heuristics.
/// </summary>
public class RelevanceClassifier
{
    /// <summary>
    /// Default set of Oman-related domain keywords for classification.
    /// </summary>
    private static readonly HashSet<string> OmanDomainKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ea.gov.om",
        "opendata.gov.om",
        "opaz.gov.om",
        "moe.gov.om",
        "moci.gov.om",
        "pw.gov.om",
        "muscatmunicipality.gov.om",
        "minhealth.gov.om",
        "omanow",
        "tashreeh"
    };

    /// <summary>
    /// General/International data source domain patterns.
    /// </summary>
    private static readonly HashSet<string> GeneralDomainPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "data.humdata.org",
        "un.org",
        "worldbank.org",
        "openstreetmap.org",
        "geoportal.un.org"
    };

    /// <summary>
    /// Classifies a data source into a relevance category.
    /// </summary>
    /// <param name="sourceUrl">The URL of the data source.</param>
    /// <param name="metadata">Optional metadata about the source.</param>
    /// <returns>A <see cref="DataSourceCategory"/> representing the classification.</returns>
    public DataSourceCategory Classify(string sourceUrl, string? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return DataSourceCategory.Unknown;

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var url))
            return DataSourceCategory.Unknown;

        var host = url.Host;

        // Check for explicit Oman domains
        if (OmanDomainKeywords.Any(k => host.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return DataSourceCategory.OmanSpecific;

        // Check for general international patterns
        if (GeneralDomainPatterns.Any(p => host.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return DataSourceCategory.General;

        // Analyze metadata for category hints
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            var lowerMeta = metadata.ToLowerInvariant();
            if (lowerMeta.Contains("oman") || lowerMeta.Contains("sultan") || lowerMeta.Contains("gcc"))
                return DataSourceCategory.OmanSpecific;
            if (lowerMeta.Contains("global") || lowerMeta.Contains("international") || lowerMeta.Contains("world"))
                return DataSourceCategory.General;
        }

        // Default to Mixed if we cannot determine with confidence
        return DataSourceCategory.Mixed;
    }

    /// <summary>
    /// Classifies multiple data sources into relevance categories.
    /// </summary>
    /// <param name="sources">Collection of source URLs with optional metadata.</param>
    /// <returns>Dictionary mapping source URLs to their category.</returns>
    public Dictionary<string, DataSourceCategory> ClassifyMany(
        IEnumerable<(string Url, string? Metadata)> sources)
    {
        var results = new Dictionary<string, DataSourceCategory>();

        foreach (var (url, metadata) in sources)
        {
            results[url] = Classify(url, metadata);
        }

        return results;
    }

    /// <summary>
    /// Calculates a confidence score for the classification.
    /// </summary>
    /// <param name="sourceUrl">The source URL.</param>
    /// <param name="metadata">Optional metadata.</param>
    /// <returns>A confidence score between 0 and 1.</returns>
    public double GetClassificationConfidence(string sourceUrl, string? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return 0.0;

        var score = 0.0;
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var url))
            return 0.0;

        var host = url.Host;
        var lowerMeta = (metadata ?? "").ToLowerInvariant();

        // Oman domain match gives high confidence
        if (OmanDomainKeywords.Any(k => host.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return 0.95;

        // General domain pattern gives moderate confidence
        if (GeneralDomainPatterns.Any(p => host.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return 0.80;

        // Metadata keyword hints
        if (lowerMeta.Contains("oman"))
            score += 0.3;
        if (lowerMeta.Contains("sultan") || lowerMeta.Contains("gcc"))
            score += 0.2;
        if (lowerMeta.Contains("global") || lowerMeta.Contains("international"))
            score += 0.1;

        // Normalize to 0-1 range
        return Math.Min(score, 1.0);
    }
}
