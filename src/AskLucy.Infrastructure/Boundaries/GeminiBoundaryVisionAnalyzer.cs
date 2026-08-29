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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini boundary vision analysis exceeded its {TimeoutSeconds}s budget; falling back to the deterministic result")]
    public static partial void TimedOut(ILogger logger, int timeoutSeconds);
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
    IOptions<BoundaryScoringOptions> boundaryOptions,
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

            // specs/043 FR-034: a budget scoped to this one call. The shared "GoogleGemini"
            // client is configured with a two-minute timeout and also serves chat, so lowering
            // that would wrongly cap chat; a linked token bounds only this call site - the same
            // pattern the Mcp client already uses. Linking to the caller's token is what keeps
            // FR-035 separable below: we can still tell who asked for the cancellation.
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(boundaryOptions.Value.VisionTimeoutSeconds));
            var visionToken = budget.Token;

            var payload = BuildPayload(image, rankedCandidates, siteName, center);
            using var response = await httpClient.PostAsJsonAsync(
                $"models/{_options.VisionModel}:generateContent?key={apiKey}", payload, visionToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(visionToken);
                GeminiBoundaryVisionAnalyzerLog.RequestFailed(logger, (int)response.StatusCode, body);
                return BoundaryVisionAnalysis.NotConfigured($"Gemini vision request failed ({(int)response.StatusCode}).");
            }

            using var stream = await response.Content.ReadAsStreamAsync(visionToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: visionToken);
            var text = ExtractText(document.RootElement);

            return string.IsNullOrWhiteSpace(text)
                ? BoundaryVisionAnalysis.NotConfigured("Gemini returned an empty vision analysis response.")
                : ParseAnalysis(text, image);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // specs/043 FR-035: the *caller* cancelled. That is a user action, not a provider
            // failure - it must propagate as cancellation and must never be recorded or
            // reported as Gemini having failed. Checked before the budget case below, because
            // when the caller cancels both tokens are cancelled and only this reading is true.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Only the budget fired: the vision call outlived FR-034's window. Degrade to the
            // deterministic result exactly like any other vision failure.
            GeminiBoundaryVisionAnalyzerLog.TimedOut(logger, boundaryOptions.Value.VisionTimeoutSeconds);
            return BoundaryVisionAnalysis.NotConfigured(
                $"Gemini vision analysis timed out after {boundaryOptions.Value.VisionTimeoutSeconds}s.");
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

            IMPORTANT RULES for selected_candidate_id:

            1. DO NOT invent new coordinates for this field.
            2. ONLY choose among the candidate IDs provided above.
            3. If none of the candidates reasonably represents the site, return null.
            4. Base your decision on visible physical features such as:
               - walls
               - fences
               - paths
               - vegetation edges
               - parking areas
               - land-use transitions
               - buildings
               - roads
               - other visible site boundaries
            5. Prefer an existing candidate over saying none fit when there is reasonable
               visual evidence supporting that candidate.

            SEPARATELY, also report where YOU visually see the site's actual boundary in this
            image, in observed_boundary_normalized — this is independent of which candidate you
            picked above, and matters most when a candidate's mapped shape looks shifted from
            where the real walls/fences/tree line actually are in the image. Report it as a list
            of [x, y] pairs, each a fraction from 0.0 to 1.0 of this image's width/height, with
            (0,0) at the image's top-left corner (x=0 is the west edge, x=1 is the east edge; y=0
            is the north edge, y=1 is the south edge). Trace the corners of the boundary you can
            actually see, in order around the perimeter (3 to 12 points). Set this to null if you
            cannot clearly identify the boundary in the image — do not guess.

            Determine which candidate ID most likely represents the actual site boundary.

            Return ONLY a JSON object with exactly these fields:

            {
                "selected_candidate_id": "<id or null>",
                "confidence": <number between 0.0 and 1.0>,
                "boundary_quality": "<high|medium|low>",
                "reasoning": ["<short reason>", "..."],
                "issues": ["<short issue>", "..."],
                "requires_refinement": <true|false>,
                "observed_boundary_normalized": [[0.12, 0.08], [0.85, 0.10], ...] or null
            }
            """;
    }

    private static BoundaryVisionAnalysis ParseAnalysis(string json, SatelliteImage image)
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
        var observedBoundary = ReadObservedBoundary(root, image);

        return new BoundaryVisionAnalysis(
            AiUsed: true, selectedId, confidence, quality, reasoning, issues, requiresRefinement, observedBoundary);
    }

    /// <summary>
    /// Converts Gemini's own image-relative read of the boundary (<c>observed_boundary_normalized</c>,
    /// [0,1] fractions of image width/height, origin top-left) back to real WGS84 coordinates using
    /// the satellite image's known geographic bounds — the same bounds given to Gemini in the
    /// prompt. This is the model's honest visual estimate, not an invented shape: it can only ever
    /// land somewhere inside the image it was shown. <see cref="BoundaryResolutionService"/> still
    /// runs a plausibility check before trusting it over mapped OSM geometry.
    /// </summary>
    private static List<GeoPoint>? ReadObservedBoundary(JsonElement root, SatelliteImage image)
    {
        if (!root.TryGetProperty("observed_boundary_normalized", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var points = new List<GeoPoint>();
        foreach (var pair in element.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2)
            {
                continue;
            }

            var x = pair[0].GetDouble();
            var y = pair[1].GetDouble();
            if (x is < 0.0 or > 1.0 || y is < 0.0 or > 1.0)
            {
                continue;
            }

            var longitude = image.West + (x * (image.East - image.West));
            var latitude = image.North - (y * (image.North - image.South));
            points.Add(new GeoPoint(latitude, longitude));
        }

        return points.Count >= 3 ? points : null;
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
