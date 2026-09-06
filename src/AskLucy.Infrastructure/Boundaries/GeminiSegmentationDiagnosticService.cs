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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AskLucy.Infrastructure.Boundaries;

internal static partial class GeminiSegmentationDiagnosticServiceLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini segmentation diagnostic failed")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini segmentation diagnostic request failed with {StatusCode}: {Body}")]
    public static partial void RequestFailed(ILogger logger, int statusCode, string body);
}

/// <summary>
/// Second diagnostic path (2026-09-06) tested against <see cref="GeminiBoundaryDrawDiagnosticService"/>'s
/// "draw the boundary" approach, after that approach proved non-deterministic across repeated live
/// calls (one run captured a real notch precisely; the next added a small hallucinated one).
/// Google's own Gemini API documents a native segmentation-mask output — a per-pixel classifier
/// result tied to a bounding box, not a generated image — matching the "vision model + segmentation,
/// not image generation" approach an independent source (the user's own ChatGPT session,
/// investigating the same problem) recommended unprompted.
/// </summary>
/// <remarks>
/// The response shape is Google's own documented convention: a JSON array of
/// <c>{"box_2d": [ymin, xmin, ymax, xmax], "label": "...", "mask": "data:image/png;base64,..."}</c>,
/// coordinates normalised 0-1000. The mask PNG is sized to the box, not the full image — it is
/// decoded, resized to the box's real pixel span, thresholded, and pasted into a full-image mask at
/// the box's offset before going through the same <see cref="MaskContourVectorizer"/> pipeline the
/// drawn-outline diagnostic uses. For visual comparison, the extracted ring is drawn back onto a
/// copy of the original map image via <see cref="PixelRingRenderer"/> — this path never generates
/// a new image itself, so there is nothing else to show a human otherwise.
/// </remarks>
internal sealed class GeminiSegmentationDiagnosticService(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleGeminiOptions> options,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    AiCapabilityProviderResolver capabilityProviderResolver,
    IAiCredentialProtector credentialProtector,
    ILogger<GeminiSegmentationDiagnosticService> logger) : IBoundarySegmentationDiagnosticService
{
    private const string ProviderKey = "google-gemini";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly Rgba32 OutlineColor = new(255, 0, 0);
    private readonly GoogleGeminiOptions _options = options.Value;

    public async Task<BoundaryDrawDiagnosticResult> SegmentAsync(
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
                GeminiSegmentationDiagnosticServiceLog.RequestFailed(logger, (int)response.StatusCode, body);
                return new BoundaryDrawDiagnosticResult(null, null, $"Gemini request failed ({(int)response.StatusCode}) for model '{modelKey}'.");
            }

            using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: budget.Token);
            var text = ExtractFirstText(document.RootElement);

            if (string.IsNullOrWhiteSpace(text))
            {
                return new BoundaryDrawDiagnosticResult(null, null, $"Model '{modelKey}' returned no content.");
            }

            return BuildResultFromSegmentationJson(text, image, siteName, modelKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            GeminiSegmentationDiagnosticServiceLog.Failed(logger, ex);
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
        ["generationConfig"] = new { responseMimeType = "application/json" },
    };

    private static string BuildPrompt(string siteName) => $"""
        You are looking at a rendered street map image (a screenshot of Google Maps, NOT a
        satellite photo). Find the shaded polygon whose label matches the site name "{siteName}".

        Give the segmentation mask for that shaded region. Output a JSON list containing exactly
        one entry, with these keys:
        - "box_2d": the region's 2D bounding box, as [ymin, xmin, ymax, xmax], integers normalised
          to 0-1000 against the full image.
        - "label": a short descriptive label.
        - "mask": the segmentation mask for that region, as a base64-encoded PNG data URI.

        Return ONLY the JSON list, nothing else.
        """;

    /// <summary>Only place this diagnostic reads free-form model text instead of the structured schema the payload requested — same as the draw diagnostic's own text-part scan.</summary>
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

    private static BoundaryDrawDiagnosticResult BuildResultFromSegmentationJson(
        string json, SatelliteImage image, string siteName, string modelKey)
    {
        using var document = JsonDocument.Parse(StripMarkdownFence(json));
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return new BoundaryDrawDiagnosticResult(null, null, $"Model '{modelKey}' did not return a segmentation entry.");
        }

        var entry = FindEntry(document.RootElement, siteName);
        if (entry is null)
        {
            return new BoundaryDrawDiagnosticResult(null, null, $"Model '{modelKey}' returned no entry matching '{siteName}'.");
        }

        if (!entry.Value.TryGetProperty("box_2d", out var box2dElement) || box2dElement.GetArrayLength() != 4
            || !entry.Value.TryGetProperty("mask", out var maskElement) || maskElement.ValueKind != JsonValueKind.String)
        {
            return new BoundaryDrawDiagnosticResult(null, null, $"Model '{modelKey}' returned an entry with no usable mask.");
        }

        var box2d = box2dElement.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var maskDataUri = maskElement.GetString();
        if (string.IsNullOrWhiteSpace(maskDataUri))
        {
            return new BoundaryDrawDiagnosticResult(null, null, $"Model '{modelKey}' returned an empty mask.");
        }

        var commaIndex = maskDataUri.IndexOf(',');
        var maskBase64 = commaIndex >= 0 ? maskDataUri[(commaIndex + 1)..] : maskDataUri;
        byte[] maskBytes;
        try
        {
            maskBytes = Convert.FromBase64String(maskBase64);
        }
        catch (FormatException)
        {
            return new BoundaryDrawDiagnosticResult(null, null, $"Model '{modelKey}' returned an unparseable mask.");
        }

        using var originalImage = Image.Load<Rgba32>(image.ImageBytes);
        var fullMask = BuildFullImageMask(maskBytes, box2d, originalImage.Width, originalImage.Height);

        var pixelRing = MaskContourVectorizer.TryExtractPixelRing(fullMask, originalImage.Width, originalImage.Height);
        if (pixelRing is null)
        {
            return new BoundaryDrawDiagnosticResult(null, null,
                $"Model '{modelKey}' returned a mask, but no plausible outline could be traced from it.");
        }

        var geoRing = MaskContourVectorizer.ToGeoRing(pixelRing, originalImage.Width, originalImage.Height, image);
        var renderedImage = PixelRingRenderer.DrawRingOnImage(image.ImageBytes, pixelRing, OutlineColor);

        return new BoundaryDrawDiagnosticResult(renderedImage, "image/jpeg", null, geoRing, "gemini-segmentation-mask");
    }

    private static JsonElement? FindEntry(JsonElement array, string siteName)
    {
        JsonElement? first = null;
        foreach (var entry in array.EnumerateArray())
        {
            first ??= entry;
            if (entry.TryGetProperty("label", out var labelElement) && labelElement.ValueKind == JsonValueKind.String
                && (labelElement.GetString() ?? "").Contains(siteName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return first;
    }

    /// <summary>
    /// Google's convention: the mask PNG is sized to <paramref name="box2d"/>'s own span, not the
    /// full image — it must be resized to the box's real pixel dimensions, thresholded, and pasted
    /// at the box's offset before it means anything against the source image's own coordinates.
    /// </summary>
    private static bool[,] BuildFullImageMask(byte[] maskPngBytes, int[] box2d, int imageWidth, int imageHeight)
    {
        var ymin = box2d[0]; var xmin = box2d[1]; var ymax = box2d[2]; var xmax = box2d[3];
        var xminPx = (int)Math.Round(xmin / 1000.0 * imageWidth);
        var yminPx = (int)Math.Round(ymin / 1000.0 * imageHeight);
        var xmaxPx = (int)Math.Round(xmax / 1000.0 * imageWidth);
        var ymaxPx = (int)Math.Round(ymax / 1000.0 * imageHeight);
        var boxWidth = Math.Max(1, xmaxPx - xminPx);
        var boxHeight = Math.Max(1, ymaxPx - yminPx);

        using var maskImage = Image.Load<L8>(maskPngBytes);
        maskImage.Mutate(ctx => ctx.Resize(boxWidth, boxHeight));

        var fullMask = new bool[imageWidth, imageHeight];
        maskImage.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var targetY = yminPx + y;
                if (targetY < 0 || targetY >= imageHeight)
                {
                    continue;
                }

                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var targetX = xminPx + x;
                    if (targetX < 0 || targetX >= imageWidth)
                    {
                        continue;
                    }

                    fullMask[targetX, targetY] = row[x].PackedValue > 127;
                }
            }
        });

        return fullMask;
    }

    /// <summary>Gemini sometimes wraps JSON in a ```json fence despite responseMimeType being set — strip it defensively.</summary>
    private static string StripMarkdownFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var withoutOpenFence = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed;
        var closeFenceIndex = withoutOpenFence.LastIndexOf("```", StringComparison.Ordinal);
        return closeFenceIndex >= 0 ? withoutOpenFence[..closeFenceIndex].Trim() : withoutOpenFence.Trim();
    }
}
