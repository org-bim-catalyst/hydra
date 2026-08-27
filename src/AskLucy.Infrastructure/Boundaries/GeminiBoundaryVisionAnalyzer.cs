using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class GeminiBoundaryVisionAnalyzerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini boundary vision request failed with {StatusCode}: {Body}")]
    public static partial void RequestFailed(ILogger logger, int statusCode, string body);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini boundary vision analysis failed")]
    public static partial void Failed(ILogger logger, Exception exception);
}

/// <summary>
/// specs/042-site-boundary-resolution — a direct port of the reference notebook's
/// <c>ai_boundary_analysis()</c>, using Gemini's multimodal <c>generateContent</c> endpoint to
/// choose among (never invent) the deterministically-ranked OSM candidates by inspecting
/// satellite imagery. Same credential-sourcing rule as <see cref="GoogleGeminiProvider"/> — reads
/// the admin-managed, encrypted credential via <see cref="IAIProviderRepository"/> +
/// <see cref="IAiCredentialProtector"/>, never a plain appsettings API key. Never throws
/// (constitution §VIII): every failure path returns <see cref="BoundaryVisionAnalysis.NotConfigured"/>.
/// </summary>
internal sealed class GeminiBoundaryVisionAnalyzer(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleGeminiOptions> options,
    IAIProviderRepository providerRepository,
    IAiCredentialProtector credentialProtector,
    ILogger<GeminiBoundaryVisionAnalyzer> logger) : IBoundaryVisionAnalyzer
{
    private const string ProviderKey = "google-gemini";
    private readonly GoogleGeminiOptions _options = options.Value;

    public async Task<BoundaryVisionAnalysis> AnalyzeAsync(
        SatelliteImage image,
        IReadOnlyList<ScoredBoundaryCandidate> rankedCandidates,
        string siteName,
        GeoPoint center,
        CancellationToken cancellationToken = default)
    {
        if (rankedCandidates.Count == 0)
        {
            return BoundaryVisionAnalysis.NotConfigured("No candidates available to analyze this run.");
        }

        try
        {
            var provider = await providerRepository.GetByKeyAsync(ProviderKey, cancellationToken);
            if (provider?.CredentialCiphertext is null)
            {
                return BoundaryVisionAnalysis.NotConfigured(
                    "Google Gemini has no credential configured — an administrator must set one to enable AI vision verification.");
            }

            var apiKey = credentialProtector.Unprotect(provider.CredentialCiphertext);
            using var httpClient = httpClientFactory.CreateClient("GoogleGemini");
            httpClient.BaseAddress = new Uri(_options.BaseUrl);

            var payload = BuildPayload(image, rankedCandidates, siteName, center);
            using var response = await httpClient.PostAsJsonAsync(
                $"models/{_options.VisionModel}:generateContent?key={apiKey}", payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                GeminiBoundaryVisionAnalyzerLog.RequestFailed(logger, (int)response.StatusCode, body);
                return BoundaryVisionAnalysis.NotConfigured($"Gemini vision request failed ({(int)response.StatusCode}).");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var text = ExtractText(document.RootElement);

            return string.IsNullOrWhiteSpace(text)
                ? BoundaryVisionAnalysis.NotConfigured("Gemini returned an empty vision analysis response.")
                : ParseAnalysis(text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            GeminiBoundaryVisionAnalyzerLog.Failed(logger, ex);
            return BoundaryVisionAnalysis.NotConfigured($"Gemini vision analysis failed: {ex.GetType().Name}.");
        }
    }

    private static Dictionary<string, object?> BuildPayload(
        SatelliteImage image, IReadOnlyList<ScoredBoundaryCandidate> rankedCandidates, string siteName, GeoPoint center) => new()
    {
        ["contents"] = new object[]
        {
            new
            {
                role = "user",
                parts = new object[]
                {
                    new { text = BuildPrompt(image, rankedCandidates, siteName, center) },
                    new { inlineData = new { mimeType = image.ContentType, data = Convert.ToBase64String(image.ImageBytes) } },
                },
            },
        },
        ["generationConfig"] = new { responseMimeType = "application/json" },
    };

    /// <summary>Ported near-verbatim from the reference notebook's own prompt text.</summary>
    private static string BuildPrompt(
        SatelliteImage image, IReadOnlyList<ScoredBoundaryCandidate> rankedCandidates, string siteName, GeoPoint center)
    {
        var candidatesDescription = string.Join('\n', rankedCandidates.Take(8).Select(c =>
        {
            var tagPreview = string.Join(", ", c.Candidate.Tags.Take(5).Select(t => $"{t.Key}={t.Value}"));
            var name = string.IsNullOrWhiteSpace(c.Candidate.Name) ? "unnamed" : c.Candidate.Name;
            return $"- id={c.Candidate.Id}, name='{name}', area={c.Candidate.AreaSquareMeters:F0} m2, " +
                   $"distance_from_center={c.Candidate.DistanceToCenterMeters:F0} m, tags={{{tagPreview}}}";
        }));

        return $$"""
            You are analyzing a satellite image to help identify the boundary of a named site.

            Site name:
            {{siteName}}

            Resolved center:
            {{center.Latitude:F6}}, {{center.Longitude:F6}}

            Image covers:
            west={{image.West:F6}}, south={{image.South:F6}}, east={{image.East:F6}}, north={{image.North:F6}}

            Candidate boundary polygons already found from OpenStreetMap:

            {{candidatesDescription}}

            IMPORTANT RULES:

            1. DO NOT invent new coordinates.
            2. DO NOT create a new polygon.
            3. ONLY choose among the candidate IDs provided above.
            4. If none of the candidates reasonably represents the site, return null.
            5. Base your decision on visible physical features such as:
               - walls
               - fences
               - paths
               - vegetation edges
               - parking areas
               - land-use transitions
               - buildings
               - roads
               - other visible site boundaries
            6. Prefer an existing candidate over saying none fit when there is reasonable
               visual evidence supporting that candidate.

            Determine which candidate ID most likely represents the actual site boundary.

            Return ONLY a JSON object with exactly these fields:

            {
                "selected_candidate_id": "<id or null>",
                "confidence": <number between 0.0 and 1.0>,
                "boundary_quality": "<high|medium|low>",
                "reasoning": ["<short reason>", "..."],
                "issues": ["<short issue>", "..."],
                "requires_refinement": <true|false>
            }
            """;
    }

    private static BoundaryVisionAnalysis ParseAnalysis(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var selectedId = root.TryGetProperty("selected_candidate_id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : null;

        double? confidence = root.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.ValueKind == JsonValueKind.Number
            ? confidenceElement.GetDouble()
            : null;

        var quality = root.TryGetProperty("boundary_quality", out var qualityElement) && qualityElement.ValueKind == JsonValueKind.String
            ? qualityElement.GetString() ?? "not_evaluated"
            : "not_evaluated";

        var reasoning = ReadStringArray(root, "reasoning");
        var issues = ReadStringArray(root, "issues");
        var requiresRefinement = root.TryGetProperty("requires_refinement", out var refinementElement)
            && refinementElement.ValueKind == JsonValueKind.True;

        return new BoundaryVisionAnalysis(
            AiUsed: true, selectedId, confidence, quality, reasoning, issues, requiresRefinement);
    }

    private static List<string> ReadStringArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList()
            : [];

    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
        {
            return string.Empty;
        }

        return string.Concat(parts.EnumerateArray()
            .Where(p => p.TryGetProperty("text", out _))
            .Select(p => p.GetProperty("text").GetString()));
    }
}
