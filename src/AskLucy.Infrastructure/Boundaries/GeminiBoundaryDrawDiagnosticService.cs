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
}

/// <summary>
/// One-off diagnostic (2026-09-06): three independent fixes to <see cref="GeminiBoundaryVisionAnalyzer"/>'s
/// text/coordinate-based prompt (removing OSM candidate coupling, removing the point ceiling,
/// widening the imagery frame) left the traced boundary unchanged, all missing the same real,
/// user-confirmed corner detail. That consistency pointed at the request shape itself: asking a
/// model to report an outline as precise normalised pixel coordinates in JSON is a fundamentally
/// different task from asking an image-generation model to draw directly on the image, which is
/// what the user's own manual ChatGPT experiment actually did.
/// </summary>
/// <remarks>
/// Reuses <c>AiCapability.BoundaryVision</c>'s existing admin assignment rather than adding a new
/// one — the user already switches that assignment between models (Gemini 3.7 Flash, Gemini Pro,
/// Nano Banana Pro) to compare results, so this reuses exactly that lever. Only an actual
/// image-generation model (the "Nano Banana" family) can populate a result's <c>ImageBytes</c>; a
/// text/vision-only model assigned to the same capability will just talk back in
/// <c>candidates[0].content.parts[].text</c>, which becomes <c>Note</c> instead.
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

            var payload = BuildPayload(image, siteName);
            using var response = await httpClient.PostAsJsonAsync(
                $"models/{modelKey}:generateContent?key={apiKey}", payload, budget.Token);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(budget.Token);
                GeminiBoundaryDrawDiagnosticServiceLog.RequestFailed(logger, (int)response.StatusCode, body);
                return new BoundaryDrawDiagnosticResult(null, null,
                    $"Gemini request failed ({(int)response.StatusCode}) for model '{modelKey}'.");
            }

            using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: budget.Token);
            return ExtractResult(document.RootElement, modelKey);
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

    private static Dictionary<string, object?> BuildPayload(SatelliteImage image, string siteName) => new()
    {
        ["contents"] = new object[]
        {
            new
            {
                role = "user",
                parts = new object[]
                {
                    new { text = BuildPrompt(siteName) },
                    new { inlineData = new { mimeType = image.ContentType, data = Convert.ToBase64String(image.ImageBytes) } },
                },
            },
        },
        ["generationConfig"] = new { responseModalities = new[] { "TEXT", "IMAGE" } },
    };

    private static string BuildPrompt(string siteName) => $"""
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

    private static BoundaryDrawDiagnosticResult ExtractResult(JsonElement root, string modelKey)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0
            || !candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
        {
            return new BoundaryDrawDiagnosticResult(null, null, $"Model '{modelKey}' returned no content.");
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
            return new BoundaryDrawDiagnosticResult(null, null, note);
        }

        return new BoundaryDrawDiagnosticResult(Convert.FromBase64String(imageBase64), contentType, string.Join(' ', noteParts));
    }
}
