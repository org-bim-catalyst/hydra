using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AskLucy.Application.DatasetSources;

/// <summary>
/// MediatR request for fetching source data from an open data URL.
/// </summary>
public class FetchSourceDataCommand : IRequest<FetchSourceDataResult>
{
    /// <summary>
    /// The URL of the data source to fetch.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional timeout in seconds for the HTTP request.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Creates a new FetchSourceDataCommand.
    /// </summary>
    /// <param name="url">The URL of the data source to fetch.</param>
    /// <param name="timeoutSeconds">Optional timeout in seconds.</param>
    public FetchSourceDataCommand(string url, int? timeoutSeconds = null)
    {
        Url = url;
        TimeoutSeconds = timeoutSeconds;
    }
}

/// <summary>
/// Result of fetching source data, containing extracted metadata and status.
/// </summary>
public class FetchSourceDataResult
{
    /// <summary>
    /// Indicates whether the fetch was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The source URL that was fetched.
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
    /// Raw HTML/content response from the source, if any.
    /// </summary>
    public string? RawContent { get; set; }

    /// <summary>
    /// Error message if the fetch failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Handler for the FetchSourceDataCommand that fetches data from an open data source URL.
/// </summary>
public class FetchSourceDataHandler : IRequestHandler<FetchSourceDataCommand, FetchSourceDataResult>
{
    private readonly IOpenDataClient _dataClient;
    private readonly ILogger<FetchSourceDataHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FetchSourceDataHandler"/> class.
    /// </summary>
    /// <param name="dataClient">The open data client for making HTTP requests.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    public FetchSourceDataHandler(IOpenDataClient dataClient, ILogger<FetchSourceDataHandler> logger)
    {
        _dataClient = dataClient;
        _logger = logger;
    }

    /// <summary>
    /// Handles the fetch request by calling the OpenDataClient and extracting metadata.
    /// </summary>
    /// <param name="request">The fetch command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the fetch operation.</returns>
    public async Task<FetchSourceDataResult> Handle(FetchSourceDataCommand request, CancellationToken cancellationToken)
    {
        var result = new FetchSourceDataResult
        {
            Url = request.Url,
            Success = false
        };

        try
        {
            // Build URI and fetch with timeout
            var uri = new Uri(request.Url);
            var timeout = request.TimeoutSeconds ?? 30;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));

            var response = await _dataClient.GetAsync(uri, timeoutCts.Token);

            if (response == null || !response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"Failed to fetch data from {request.Url}. Status: {response?.StatusCode}";
                _logger.LogWarning(result.ErrorMessage);
                return result;
            }

            result.RawContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // Try to extract basic metadata using JsonSerializer or HTML parsing
            ExtractMetadata(result, response.Content.Headers.ContentType?.MediaType);

            result.Success = true;
            _logger.LogInformation("Successfully fetched data from {Url}", request.Url);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error fetching data from {Url}", request.Url);
        }

        return result;
    }

    /// <summary>
    /// Extracts basic metadata (title, description, last-updated) from the response content.
    /// </summary>
    /// <param name="result">The result to populate.</param>
    /// <param name="contentType">The media type of the content.</param>
    private void ExtractMetadata(FetchSourceDataResult result, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(result.RawContent))
            return;

        try
        {
            // Try JSON deserialization first
            if (IsJsonContent(contentType))
            {
                using JsonDocument doc = JsonDocument.Parse(result.RawContent);
                var root = doc.RootElement;

                // Try common JSON field names across different data sources
                result.Title = GetJsonProperty(doc, "name", "title", "label");
                result.Description = GetJsonProperty(doc, "description", "summary", "abstract");
                result.LastUpdated = ParseJsonDate(doc, "lastUpdated", "lastModified", "timestamp", "date");
            }
            else
            {
                // Try HTML metadata extraction
                ExtractHtmlMetadata(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract metadata from {Url}", result.Url);
            // Don't fail the whole fetch if metadata extraction fails
            result.RawContent = result.RawContent?.Substring(0, Math.Min(result.RawContent?.Length ?? 0, 5000));
        }
    }

    /// <summary>
    /// Checks if the content type is JSON.
    /// </summary>
    /// <param name="contentType">The media type.</param>
    /// <returns>True if the content is JSON.</returns>
    private bool IsJsonContent(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        return contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("application/json-seq", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a JSON property value by trying multiple possible field names.
    /// </summary>
    /// <param name="doc">The JsonDocument.</param>
    /// <param name="fieldNames">Possible field names to try.</param>
    /// <returns>The value, or null if not found.</returns>
    private static string? GetJsonProperty(JsonDocument doc, params string[] fieldNames)
    {
        foreach (var field in fieldNames)
        {
            if (doc.RootElement.TryGetProperty(field, out var prop))
                return prop.GetString();
        }
        return null;
    }

    /// <summary>
    /// Parses a date from a JSON document using multiple possible field names.
    /// </summary>
    /// <param name="doc">The JsonDocument.</param>
    /// <param name="fieldNames">Possible date field names.</param>
    /// <returns>The parsed DateTime, or null if parsing fails.</returns>
    private static DateTime? ParseJsonDate(JsonDocument doc, params string[] fieldNames)
    {
        foreach (var field in fieldNames)
        {
            if (doc.RootElement.TryGetProperty(field, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (DateTime.TryParse(prop.GetString(), out var date))
                    return date;
            }
        }
        return null;
    }

    /// <summary>
    /// Extracts metadata from HTML content using common meta tags.
    /// </summary>
    /// <param name="result">The result to populate.</param>
    private void ExtractHtmlMetadata(FetchSourceDataResult result)
    {
        // Simple HTML meta tag extraction
        var content = result.RawContent;

        // Try to find title tag
        var titleMatch = System.Text.RegularExpressions.Regex.Match(content, @"<title[^>]*>([^<]*)</title>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (titleMatch.Success && !string.IsNullOrWhiteSpace(titleMatch.Groups[1].Value))
            result.Title = titleMatch.Groups[1].Value;

        // Try to find meta description
        var descMatch = System.Text.RegularExpressions.Regex.Match(content, "<meta[^>]*name=[\"']description[\"'][^>]*content=[\"']?([^\"']*)[\"']?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (descMatch.Success && !string.IsNullOrWhiteSpace(descMatch.Groups[1].Value))
            result.Description = descMatch.Groups[1].Value;

        // Try to find meta last-updated
        var dateMatch = System.Text.RegularExpressions.Regex.Match(content, "<meta[^>]*property=[\"']og:updated[\"'][^>]*content=[\"']?([^\"']*)[\"']?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, out var date))
            result.LastUpdated = date;

        var lastModifiedMatch = System.Text.RegularExpressions.Regex.Match(content, "<meta[^>]*name=[\"']last-modified[\"'][^>]*content=[\"']?([^\"']*)[\"']?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (lastModifiedMatch.Success && DateTime.TryParse(lastModifiedMatch.Groups[1].Value, out var lmDate))
            result.LastUpdated = lmDate;
    }
}
