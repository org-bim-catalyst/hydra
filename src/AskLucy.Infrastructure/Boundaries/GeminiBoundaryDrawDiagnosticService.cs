using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Ai;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class GeminiBoundaryDrawDiagnosticServiceLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini boundary-draw diagnostic failed")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini boundary-draw diagnostic request failed with {StatusCode}: {Body}")]
    public static partial void RequestFailed(ILogger logger, int statusCode, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Boundary-draw diagnostic: pixel extraction found no plausible outline, falling back to an AI line-read")]
    public static partial void FallingBackToAiLineRead(ILogger logger);
}

/// <summary>
/// One-off diagnostic (2026-09-06): three independent fixes to <see cref="GeminiBoundaryVisionAnalyzer"/>'s
/// text/coordinate-based prompt (removing OSM candidate coupling, removing the point ceiling,
/// widening the imagery frame) left the traced boundary unchanged, all missing the same real,
/// user-confirmed corner detail. That consistency pointed at the request shape itself: asking a
/// model to report an outline as precise normalised pixel coordinates in JSON is a fundamentally
/// different task from asking an image-generation model to draw directly on the image, which is
/// what the user's own manual ChatGPT experiment actually did — and which, tested live, captured
/// the notch on the first try.
/// </summary>
/// <remarks>
/// <para>
/// Reuses <c>AiCapability.BoundaryVision</c>'s existing admin assignment rather than adding a new
/// one — the user already switches that assignment between models (Gemini 3.7 Flash, Gemini Pro,
/// Nano Banana Pro) to compare results, so this reuses exactly that lever. Only an actual
/// image-generation model (the "Nano Banana" family) can populate a result's <c>ImageBytes</c>; a
/// text/vision-only model assigned to the same capability will just talk back in
/// <c>candidates[0].content.parts[].text</c>, which becomes <c>Note</c> instead.
/// </para>
/// <para>
/// Once an image comes back, <see cref="RedOutlineVectorizer"/> tries to turn the drawn red line
/// into real coordinates deterministically — no model call, no chance of the same coordinate-
/// precision failure this diagnostic exists to route around. Only when that finds nothing
/// plausible (wrong red threshold, a shape too small, edge topology that never closes) does this
/// fall back to a second AI call — critically, one that asks the model to report the path of a
/// line it can already see drawn in front of it, not to infer a semantic boundary from scratch.
/// That is a mechanically much easier task, even though it is the same "coordinates from a model"
/// request shape that failed for the harder task.
/// </para>
/// </remarks>
internal sealed class GeminiBoundaryDrawDiagnosticService(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleGeminiOptions> options,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    AiCapabilityProviderResolver capabilityProviderResolver,
    IAiCredentialProtector credentialProtector,
    ILogger<GeminiBoundaryDrawDiagnosticService> logger) : IBoundaryDrawDiagnosticService
{
    private const string ProviderKey = "google-gemini";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);
    private readonly GoogleGeminiOptions _options = options.Value;

    public async Task<BoundaryDrawDiagnosticResult> DrawAsync(
        SatelliteImage image, string siteName, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolved = await capabilityProviderResolver.ResolveAsync(AiCapability.BoundaryVision, cancellationToken);
            var provider = await providerRepository.GetByIdAsync(resolved.ProviderId, cancellationToken);

            if (provider is null || !string.Equals(provider.ProviderKey, ProviderKey, StringComparison.Ordinal))
            {
                return new BoundaryDrawDiagnosticResult(null, null,
                    $"This diagnostic reuses the BoundaryVision capability's assignment, which currently points at " +
                    $"'{provider?.DisplayName ?? "none"}' — a Google Gemini provider must be assigned first.");
            }

            if (provider.CredentialCiphertext is null)
            {
                return new BoundaryDrawDiagnosticResult(null, null,
                    "Google Gemini has no credential configured — an administrator must set one first.");
            }

            var model = await modelRepository.GetByIdAsync(resolved.ModelId, cancellationToken);
            var modelKey = model?.ModelKey ?? _options.VisionModel;

            var apiKey = credentialProtector.Unprotect(provider.CredentialCiphertext);
            using var httpClient = httpClientFactory.CreateClient("GoogleGemini");
            httpClient.BaseAddress = new Uri(_options.BaseUrl);

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(RequestTimeout);

            var drawPayload = BuildDrawPayload(image, siteName);
            var (drawnImageBytes, drawnContentType, drawNote) = await PostForImageAsync(
                httpClient, modelKey, apiKey, drawPayload, budget.Token);

            if (drawnImageBytes is null)
            {
                return new BoundaryDrawDiagnosticResult(null, null, drawNote);
            }

            var vertices = RedOutlineVectorizer.TryExtractRing(drawnImageBytes, image);
            if (vertices is not null)
            {
                return new BoundaryDrawDiagnosticResult(
                    drawnImageBytes, drawnContentType, drawNote, vertices, "pixel-extraction");
            }

            GeminiBoundaryDrawDiagnosticServiceLog.FallingBackToAiLineRead(logger);
            var fallbackVertices = await TryReadDrawnLineAsync(
                httpClient, modelKey, apiKey, drawnImageBytes, drawnContentType ?? "image/png", image, budget.Token);

            return new BoundaryDrawDiagnosticResult(
                drawnImageBytes, drawnContentType, drawNote,
                fallbackVertices, fallbackVertices is not null ? "ai-line-read" : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            GeminiBoundaryDrawDiagnosticServiceLog.Failed(logger, ex);
            return new BoundaryDrawDiagnosticResult(null, null, $"Diagnostic call failed: {ex.GetType().Name}.");
        }
    }

    private async Task<(byte[]? ImageBytes, string? ContentType, string? Note)> PostForImageAsync(
        HttpClient httpClient, string modelKey, string apiKey, Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"models/{modelKey}:generateContent?key={apiKey}", payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            GeminiBoundaryDrawDiagnosticServiceLog.RequestFailed(logger, (int)response.StatusCode, body);
            return (null, null, $"Gemini request failed ({(int)response.StatusCode}) for model '{modelKey}'.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ExtractImageAndText(document.RootElement, modelKey);
    }

    private static Dictionary<string, object?> BuildDrawPayload(SatelliteImage image, string siteName) => new()
    {
        ["contents"] = new object[]
        {
            new
            {
                role = "user",
                parts = new object[]
                {
                    new { text = BuildDrawPrompt(siteName) },
                    new { inlineData = new { mimeType = image.ContentType, data = Convert.ToBase64String(image.ImageBytes) } },
                },
            },
        },
        ["generationConfig"] = new { responseModalities = new[] { "TEXT", "IMAGE" } },
    };

    private static string BuildDrawPrompt(string siteName) => $"""
        You are looking at a rendered street map image (a screenshot of Google Maps, NOT a
        satellite photo). Find the shaded polygon whose label matches the site name "{siteName}".

        Draw a single, solid, bright red outline directly on top of this image, tracing that
        polygon's true boundary exactly as it is drawn on the map — every corner, every notch,
        every curve, precisely as rendered. Do not simplify it into a rectangle or smooth over any
        detail; if the real outline bends or steps at a point, your line must bend or step there
        too.

        Do not change anything else about the image: keep every label, street, building, and
        shape exactly as it already appears. Add nothing but that one outline. Return the
        resulting image.
        """;

    /// <summary>
    /// Option B, tried only when pixel extraction finds nothing plausible: the model is shown the
    /// image it just produced and asked to report the path of the line already drawn on it — a
    /// mechanical read of an explicit, high-contrast marker, not a semantic boundary inference.
    /// </summary>
    private static async Task<IReadOnlyList<GeoPoint>?> TryReadDrawnLineAsync(
        HttpClient httpClient, string modelKey, string apiKey, byte[] annotatedImageBytes, string contentType,
        SatelliteImage bounds, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["contents"] = new object[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = LineReadPrompt },
                        new { inlineData = new { mimeType = contentType, data = Convert.ToBase64String(annotatedImageBytes) } },
                    },
                },
            },
            ["generationConfig"] = new { responseMimeType = "application/json" },
        };

        using var response = await httpClient.PostAsJsonAsync(
            $"models/{modelKey}:generateContent?key={apiKey}", payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var text = ExtractFirstText(document.RootElement);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return ReadLineNormalized(text, bounds);
    }

    private const string LineReadPrompt = """
        There is already a single red outline drawn on this image. Do not evaluate whether it is
        correct or trace anything new — just report the path of that exact red line, as it already
        appears, as a list of [x, y] pairs, each a fraction from 0.0 to 1.0 of the image's
        width/height (0,0 = top-left, x=east, y=south). One point per corner or bend in the red
        line, in order around its perimeter, closed loop (do not repeat the first point at the
        end). Return ONLY this JSON object:

        { "line_normalized": [[0.12, 0.08], [0.85, 0.10], ...] or null }
        """;

    private static List<GeoPoint>? ReadLineNormalized(string json, SatelliteImage bounds)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("line_normalized", out var element) || element.ValueKind != JsonValueKind.Array)
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

            var longitude = bounds.West + (x * (bounds.East - bounds.West));
            var latitude = bounds.North - (y * (bounds.North - bounds.South));
            points.Add(new GeoPoint(latitude, longitude));
        }

        if (points.Count < 3)
        {
            return null;
        }

        if (points[0].Latitude != points[^1].Latitude || points[0].Longitude != points[^1].Longitude)
        {
            points.Add(points[0]);
        }

        return points;
    }

    private static (byte[]? ImageBytes, string? ContentType, string? Note) ExtractImageAndText(JsonElement root, string modelKey)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0
            || !candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
        {
            return (null, null, $"Model '{modelKey}' returned no content.");
        }

        string? imageBase64 = null;
        string? contentType = null;
        var noteParts = new List<string>();

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("inlineData", out var inlineData)
                && inlineData.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String)
            {
                imageBase64 = dataElement.GetString();
                contentType = inlineData.TryGetProperty("mimeType", out var mime) ? mime.GetString() : "image/png";
            }
            else if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
            {
                noteParts.Add(textElement.GetString() ?? "");
            }
        }

        if (imageBase64 is null)
        {
            var note = noteParts.Count > 0
                ? string.Join(' ', noteParts)
                : $"Model '{modelKey}' did not return an image — it may not support image output.";
            return (null, null, note);
        }

        return (Convert.FromBase64String(imageBase64), contentType, noteParts.Count > 0 ? string.Join(' ', noteParts) : null);
    }

    private static string? ExtractFirstText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0
            || !candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
        {
            return null;
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
            {
                return textElement.GetString();
            }
        }

        return null;
    }
}
