using AskLucy.Application.Abstractions;

namespace AskLucy.Application.DatasetSources;

/// <summary>
> Data Transfer Object for evaluation results, containing fetched source metadata and classification information.
> </Summary>
/// <Value>Used by the evaluation pipeline to present source assessment results to the user.</Value>
public class EvaluationResult
{
    /// <summary>
    /// The source URL that was evaluated.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The title of the data source, if extractable.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The description of the data source, if extractable.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The last-updated timestamp, if extractable.
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// The relevance category of the data source.
    /// </summary>
    public RelevanceClassifier.DataSourceCategory Category { get; set; } = RelevanceClassifier.DataSourceCategory.Unknown;

    /// <summary>
    /// Confidence score for the classification (0-1).
    /// </summary>
    public double Confidence { get; set; } = 0.0;

    /// <summary>
    /// Raw content from the source, if any.
    /// </summary>
    public string? RawContent { get; set; }

    /// <summary>
    /// Error message, if the evaluation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Date and time when the evaluation was performed (UTC).
    /// </summary>
    public DateTime EvaluatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a new EvaluationResult from a FetchSourceDataResult.
    /// </summary>
    /// <param name="fetchResult">The fetch result to map from.</param>
    /// <param name="&#34;assessResult&#34;">The relevance assessment result to map from.</param>
    public EvaluationResult(FetchSourceDataResult fetchResult, AssessRelevanceResult? assessResult = null)
    {
        Url = fetchResult.Url;
        Title = fetchResult.Title;
        Description = fetchResult.Description;
        LastUpdated = fetchResult.LastUpdated;
        RawContent = fetchResult.RawContent;
        ErrorMessage = fetchResult.ErrorMessage;
        EvaluatedAtUtc = DateTime.UtcNow;

        // Map relevance assessment if available
        if (assessResult != null)
        {
            Category = assessResult.Category;
            Confidence = assessResult.Confidence;
        }
        else
        {
            // Default classification based on URL alone
            var classifier = new RelevanceClassifier();
            Category = classifier.Classify(Url);
            Confidence = classifier.GetClassificationConfidence(Url);
        }
    }
}