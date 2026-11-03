using AskLucy.Application.Abstractions;
using AskLucy.Domain.DatasetSources;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace AskLucy.Application.DatasetSources;

/// <summary>
/// MediatR request for assessing the relevance of a data source.
/// </summary>
public class AssessRelevanceQuery : IRequest<AssessRelevanceResult>
{
    /// <summary>
    /// The URL of the data source to assess.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional metadata about the source.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Creates a new AssessRelevanceQuery.
    /// </summary>
    /// <param name="url">The URL of the data source to assess.</param>
    /// <param name="metadata">Optional metadata about the source.</param>
    public AssessRelevanceQuery(string url, string? metadata = null)
    {
        Url = url;
        Metadata = metadata;
    }
}

/// <summary>
/// Result of assessing data source relevance, including category and confidence score.
/// </summary>
public class AssessRelevanceResult
{
    /// <summary>
    /// The relevance category of the data source.
    /// </summary>
    public DataSourceCategory Category { get; set; } = DataSourceCategory.Unknown;

    /// <summary>
    /// Confidence score for the classification (0-1).
    /// </summary>
    public double Confidence { get; set; } = 0.0;

    /// <summary>
    /// The source URL that was assessed.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Rationale for the categorization.
    /// </summary>
    public string Rationale { get; set; } = string.Empty;
}

/// <summary>
/// Handler for the AssessRelevanceQuery that classifies a data source.
/// </summary>
public class AssessRelevanceHandler : IRequestHandler<AssessRelevanceQuery, AssessRelevanceResult>
{
    private readonly RelevanceClassifier _classifier;
    private readonly ILogger<AssessRelevanceHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssessRelevanceHandler"/> class.
    /// </summary>
    /// <param name="classifier">The RelevanceClassifier for categorization.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    public AssessRelevanceHandler(RelevanceClassifier classifier, ILogger<AssessRelevanceHandler> logger)
    {
        _classifier = classifier;
        _logger = logger;
    }

    /// <summary>
    /// Handles the relevance assessment request by classifying the data source.
    /// </summary>
    /// <param name="request">The assess relevance command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the relevance assessment.</returns>
    public async Task<AssessRelevanceResult> Handle(AssessRelevanceQuery request, CancellationToken cancellationToken)
    {
        var result = new AssessRelevanceResult
        {
            Url = request.Url,
            Rationale = "Automated classification based on domain analysis and metadata analysis"
        };

        try
        {
            // Classify the source using the RelevanceClassifier
            var category = _classifier.Classify(request.Url, request.Metadata);
            result.Category = category;

            // Get confidence score
            result.Confidence = _classifier.GetClassificationConfidence(request.Url, request.Metadata);

            // Build rationale based on the category
            switch (category)
            {
                case DataSourceCategory.OmanSpecific:
                    result.Rationale = "Source classified as Oman-specific based on domain analysis (ea.gov.om, opendata.gov.om, etc.) and/or metadata containing Oman-related keywords.";
                    break;
                case DataSourceCategory.General:
                    result.Rationale = "Source classified as General/International based on domain patterns (data.humdata.org, un.org, etc.) and/or metadata indicating global coverage.";
                    break;
                case DataSourceCategory.Mixed:
                    result.Rationale = "Source classified as Mixed relevance, containing both Oman-specific and general content based on domain and metadata analysis.";
                    break;
                case DataSourceCategory.Unknown:
                    result.Rationale = "Source could not be classified with confidence; further manual review recommended.";
                    break;
            }

            _logger.LogInformation("Assessed relevance for {Url}: Category={Category}, Confidence={Confidence}",
                request.Url, category, result.Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assessing relevance for {Url}", request.Url);
            result.Category = DataSourceCategory.Unknown;
            result.Confidence = 0.0;
            result.Rationale = $"Error during classification: {ex.Message}";
        }

        return result;
    }
}
