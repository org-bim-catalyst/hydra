using System.Net;
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

internal static partial class GeminiBoundaryVisionAnalyzerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini boundary vision request failed with {StatusCode}: {Body}")]
    public static partial void RequestFailed(ILogger logger, int statusCode, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Gemini boundary vision returned {StatusCode}; retrying (attempt {Attempt} of {MaxAttempts})")]
    public static partial void RetryingAfterTransientFailure(ILogger logger, int statusCode, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini boundary vision analysis failed")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini boundary vision analysis exceeded its {TimeoutSeconds}s budget; falling back to the deterministic result")]
    public static partial void TimedOut(ILogger logger, int timeoutSeconds);
}

/// <summary>
/// specs/042-site-boundary-resolution — a direct port of the reference notebook's
/// <c>ai_boundary_analysis()</c>, using Gemini's multimodal <c>generateContent</c> endpoint to
/// choose among (never invent) the deterministically-ranked OSM candidates by inspecting a
/// rendered street-map image (Google's own drawn polygon for the site, not a satellite photo —
/// see <see cref="GoogleSatelliteImageProvider"/>'s remarks for why). Same credential-sourcing
/// rule as <see cref="GoogleGeminiProvider"/> — reads
/// the admin-managed, encrypted credential via <see cref="IAIProviderRepository"/> +
/// <see cref="IAiCredentialProtector"/>, never a plain appsettings API key. Never throws
/// (constitution §VIII): every failure path returns <see cref="BoundaryVisionAnalysis.NotConfigured"/>.
/// </summary>
internal sealed class GeminiBoundaryVisionAnalyzer(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleGeminiOptions> options,
    IOptions<BoundaryScoringOptions> boundaryOptions,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    AiCapabilityProviderResolver capabilityProviderResolver,
    IAiCredentialProtector credentialProtector,
    ILogger<GeminiBoundaryVisionAnalyzer> logger) : IBoundaryVisionAnalyzer
{
    private const string ProviderKey = "google-gemini";
    private readonly GoogleGeminiOptions _options = options.Value;

    public async Task<BoundaryVisionAnalysis> AnalyzeAsync(
        SatelliteImage image,
        IReadOnlyList<StreetViewImage> streetViews,
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
            // Which provider serves BoundaryVision, and which of its models, is now an
            // administrator setting rather than the constant this class used to hardcode
            // alongside a model name read from appsettings. Two places nobody could see,
            // for a capability the operator was never asked about.
            var resolved = await capabilityProviderResolver.ResolveAsync(AiCapability.BoundaryVision, cancellationToken);
            var provider = await providerRepository.GetByIdAsync(resolved.ProviderId, cancellationToken);

            // The request/response shape below is Gemini's generateContent contract. Assigning
            // a provider with no vision implementation is reported plainly instead of being
            // silently served by Gemini anyway — the caller degrades to the deterministic
            // boundary either way, but the administrator can now see why.
            if (provider is null || !string.Equals(provider.ProviderKey, ProviderKey, StringComparison.Ordinal))
            {
                return BoundaryVisionAnalysis.NotConfigured(
                    $"AI vision verification currently requires a Google Gemini provider; '{provider?.DisplayName ?? "none"}' is assigned to the boundary-vision capability.");
            }

            if (provider.CredentialCiphertext is null)
            {
                return BoundaryVisionAnalysis.NotConfigured(
                    "Google Gemini has no credential configured — an administrator must set one to enable AI vision verification.");
            }

            var visionModel = await modelRepository.GetByIdAsync(resolved.ModelId, cancellationToken);
            var visionModelKey = visionModel?.ModelKey ?? _options.VisionModel;

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
            var deadline = DateTimeOffset.UtcNow.AddSeconds(boundaryOptions.Value.VisionTimeoutSeconds);

            var payload = BuildPayload(image, streetViews, rankedCandidates, siteName, center);

            // Retried, because the failure that actually happens here is transient. Every boundary
            // request on 2026-08-31 came back 503 UNAVAILABLE — "This model is currently
            // experiencing high demand. Spikes in demand are usually temporary." — and a single
            // attempt turned each one into a silently uncorrected boundary. Same shape as the
            // retry OverpassBoundaryCandidateProvider already carries for the same reason.
            //
            // The whole loop still runs inside the vision budget, so retrying can delay the turn
            // by the delays below but never past VisionTimeoutSeconds.
            HttpResponseMessage? response = null;
            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    response?.Dispose();
                    response = await httpClient.PostAsJsonAsync(
                        $"models/{visionModelKey}:generateContent?key={apiKey}", payload, visionToken);

                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }

                    var body = await response.Content.ReadAsStringAsync(visionToken);
                    var status = (int)response.StatusCode;
                    GeminiBoundaryVisionAnalyzerLog.RequestFailed(logger, status, body);

                    // A retry is only worth starting if the budget can actually pay for it.
                    // On 2026-08-31 a fast 503 was retried with 27 s left, the second attempt ran
                    // until the budget expired, and the turn spent that time to reach the same
                    // answer it already had. Failing fast is better than failing slowly.
                    var remaining = deadline - DateTimeOffset.UtcNow - (RetryDelay * attempt);
                    if (attempt >= MaxAttempts || !IsTransient(response.StatusCode) || remaining < MinimumBudgetForRetry)
                    {
                        return BoundaryVisionAnalysis.NotConfigured($"Gemini vision request failed ({status}).");
                    }

                    GeminiBoundaryVisionAnalyzerLog.RetryingAfterTransientFailure(logger, status, attempt, MaxAttempts);
                    await Task.Delay(RetryDelay * attempt, visionToken);
                }

                using var stream = await response.Content.ReadAsStreamAsync(visionToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: visionToken);
                var text = ExtractText(document.RootElement);

                return string.IsNullOrWhiteSpace(text)
                    ? BoundaryVisionAnalysis.NotConfigured("Gemini returned an empty vision analysis response.")
                    : ParseAnalysis(text, image);
            }
            finally
            {
                response?.Dispose();
            }
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

    /// <summary>
    /// The rendered map image is always parts[0]/[1] (prompt text, then the image) so
    /// <c>observed_boundary_normalized</c>'s frame of reference is unambiguous. Street views, if
    /// any, follow as their own text-then-image pairs — labelled with heading and approximate
    /// position so the model can relate what it sees on the ground back to a specific edge of the
    /// map, without ever being asked to produce coordinates from them.
    /// </summary>
    private static Dictionary<string, object?> BuildPayload(
        SatelliteImage image, IReadOnlyList<StreetViewImage> streetViews,
        IReadOnlyList<ScoredBoundaryCandidate> rankedCandidates, string siteName, GeoPoint center)
    {
        var parts = new List<object>
        {
            new { text = BuildPrompt(image, streetViews, rankedCandidates, siteName, center) },
            new { inlineData = new { mimeType = image.ContentType, data = Convert.ToBase64String(image.ImageBytes) } },
        };

        if (streetViews.Count > 0)
        {
            parts.Add(new
            {
                text = "The following images are ground-level Street View photos, each taken near the " +
                       "site's perimeter and aimed back toward it at the stated compass heading (0=north, " +
                       "90=east, 180=south, 270=west). Use them only as extra context, if useful — they show " +
                       "what is actually on the ground, which can help you tell which shaded shape on the map " +
                       "is really the named site when the map is ambiguous. Never report coordinates from " +
                       "these photos: observed_boundary_normalized is always relative to the rendered map " +
                       "image only.",
            });

            foreach (var streetView in streetViews)
            {
                parts.Add(new
                {
                    text = $"Ground-level photo, heading {streetView.HeadingDegrees:F0} degrees, " +
                           $"taken near ({streetView.ViewpointLatitude:F6}, {streetView.ViewpointLongitude:F6}):",
                });
                parts.Add(new { inlineData = new { mimeType = streetView.ContentType, data = Convert.ToBase64String(streetView.ImageBytes) } });
            }
        }

        return new()
        {
            ["contents"] = new object[]
            {
                new { role = "user", parts = parts.ToArray() },
            },
            ["generationConfig"] = new { responseMimeType = "application/json" },
        };
    }

    /// <summary>Ported near-verbatim from the reference notebook's own prompt text.</summary>
    private static string BuildPrompt(
        SatelliteImage image, IReadOnlyList<StreetViewImage> streetViews,
        IReadOnlyList<ScoredBoundaryCandidate> rankedCandidates, string siteName, GeoPoint center)
    {
        var candidatesDescription = string.Join('\n', rankedCandidates.Take(8).Select(c =>
        {
            var tagPreview = string.Join(", ", c.Candidate.Tags.Take(5).Select(t => $"{t.Key}={t.Value}"));
            var name = string.IsNullOrWhiteSpace(c.Candidate.Name) ? "unnamed" : c.Candidate.Name;
            return $"- id={c.Candidate.Id}, name='{name}', area={c.Candidate.AreaSquareMeters:F0} m2, " +
                   $"distance_from_center={c.Candidate.DistanceToCenterMeters:F0} m, tags={{{tagPreview}}}";
        }));

        var streetViewNote = streetViews.Count > 0
            ? $"\n{streetViews.Count} ground-level Street View photo(s) near this site follow the map image below - use them only as extra context if the map itself is ambiguous about which shape is the named site.\n"
            : "";

        return $$"""
            You are looking at a rendered street map image (a screenshot of Google Maps, NOT a
            satellite photo) to help identify the boundary of a named site. Google draws many
            places of interest — parks, complexes, campuses — as a solid shaded polygon with a
            crisp edge and its own label, directly on the map. Your job has two independent parts.
            {{streetViewNote}}
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
            4. Base your decision on what the map itself shows:
               - a shaded/coloured area whose label matches the site name
               - paths, parking areas, and buildings drawn inside or around it
               - roads that run along its edges
               - sub-markers or labels for facilities that belong to the site
            5. Prefer an existing candidate over saying none fit when there is reasonable
               visual evidence supporting that candidate.

            SEPARATELY, also trace the actual shape Google has drawn on the map for this named
            site, in observed_boundary_normalized — this is independent of which candidate you
            picked above, and matters most when a candidate's mapped shape does not line up with
            the shaded area or label the map itself shows. Find the shaded polygon (or, if the site
            has no shaded fill, the shape most clearly implied by its label, paths, and
            surroundings) and trace its outline as drawn.

            Do this by walking the actual edge, not by drawing a rough bounding shape around it.
            Real sites are very often NOT simple rectangles or straight-sided polygons — the true
            boundary can bend sharply, step in or out where another feature meets it, or curve or
            wander in an organic, irregular line with no straight segments at all. You are not
            limited to placing points only at sharp corners: wherever the edge curves or bends
            gradually rather than at a single corner, lay down enough points along that curve to
            follow it closely, the way you would trace it by hand with a fine pencil rather than a
            ruler. Treat every part of the boundary as a place that might curve or step until you
            have actually looked at it — do not assume straight lines or a small, tidy number of
            corners by default. There is no limit on how many points you use: a genuinely
            rectangular site needs only 4, a site with one notch might need 6-8, and a site with a
            long curved or irregular edge could reasonably need several dozen — use however many
            it actually takes to match what is drawn, not a round or convenient number. Ground-level
            photos, if included below, are only for resolving which shape on the map is the right
            one when that is unclear; report the traced shape's position in the map image's own
            frame regardless. Report it as a list of [x, y] pairs, each a fraction from 0.0 to 1.0
            of the map image's width/height, with (0,0) at its top-left corner (x=0 is the west
            edge, x=1 is the east edge; y=0 is the north edge, y=1 is the south edge). List each
            point once, in order around the perimeter — do not repeat the first point at the end,
            the loop is closed automatically. Set this to null if you cannot clearly identify a
            drawn shape for this site — do not guess.

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

    /// <summary>Attempts per boundary, including the first.</summary>
    private const int MaxAttempts = 3;

    /// <summary>Multiplied by the attempt number, so the waits are ~1s then ~2s.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Budget a retry needs left on the clock to be worth starting at all.</summary>
    private static readonly TimeSpan MinimumBudgetForRetry = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Statuses worth a second attempt: the model being busy or rate-limited says nothing about
    /// whether the request was valid. A 4xx other than 429 is our fault and will fail identically
    /// however many times it is sent.
    /// </summary>
    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable      // 503 — the one actually seen
            or HttpStatusCode.TooManyRequests            // 429
            or HttpStatusCode.InternalServerError        // 500
            or HttpStatusCode.BadGateway                 // 502
            or HttpStatusCode.GatewayTimeout;            // 504

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
    /// Converts Gemini's own image-relative trace of the boundary (<c>observed_boundary_normalized</c>,
    /// [0,1] fractions of image width/height, origin top-left) back to real WGS84 coordinates using
    /// the map image's known geographic bounds — the same bounds given to Gemini in the prompt.
    /// This is a trace of a shape Google itself already drew, not an invented one: it can only
    /// ever land somewhere inside the image it was shown. <see cref="BoundaryResolutionService"/>
    /// still runs a plausibility check before trusting it over mapped OSM geometry.
    /// </summary>
    /// <remarks>
    /// The returned ring is always explicitly closed (first point repeated as the last), matching
    /// the convention every other polygon in this codebase already uses (OSM candidate rings,
    /// <c>ConfirmedSiteBoundaryData.Polygon</c>). The prompt asks the model not to repeat the first
    /// point itself, but nothing stops it doing so anyway or leaving the ring open — closing it
    /// here, once, is cheaper than trusting either behaviour from a model response.
    /// </remarks>
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

        if (points.Count < 3)
        {
            return null;
        }

        const double ClosureToleranceDegrees = 1e-9;
        var first = points[0];
        var last = points[^1];
        if (Math.Abs(first.Latitude - last.Latitude) > ClosureToleranceDegrees
            || Math.Abs(first.Longitude - last.Longitude) > ClosureToleranceDegrees)
        {
            points.Add(first);
        }

        return points;
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
