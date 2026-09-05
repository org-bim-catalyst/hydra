using System.Net;
using System.Text;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Ai;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Ai;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>
/// specs/042-site-boundary-resolution post-release follow-up — <see cref="GeminiBoundaryVisionAnalyzer"/>
/// parses the candidate-selection fields (ported from the reference notebook) and the
/// user-requested <c>observed_boundary_normalized</c> geo-referencing addition, and degrades to
/// <see cref="BoundaryVisionAnalysis.NotConfigured"/> on every failure path rather than throwing.
/// </summary>
public sealed class GeminiBoundaryVisionAnalyzerTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IAiCapabilityAssignmentRepository _assignments = Substitute.For<IAiCapabilityAssignmentRepository>();
    private readonly IAiCredentialProtector _credentialProtector = Substitute.For<IAiCredentialProtector>();

    private static readonly GeoPoint Center = new(25.1560, 55.2218);
    private static readonly SatelliteImage Image = new([1, 2, 3], "image/png", West: 55.2200, South: 25.1540, East: 55.2240, North: 25.1580);
    private static readonly IReadOnlyList<ScoredBoundaryCandidate> Candidates =
    [
        new ScoredBoundaryCandidate(
            new BoundaryCandidate("osm_1", new SiteBoundaryPolygon([new GeoPoint(25.156, 55.221), new GeoPoint(25.157, 55.222), new GeoPoint(25.155, 55.222)]),
                SiteBoundarySource.OsmBoundary, "Al Safa Park 2", new Dictionary<string, string> { ["leisure"] = "park" }, 5, 15_000),
            0.9, new Dictionary<string, double>()),
    ];

    public GeminiBoundaryVisionAnalyzerTests()
    {
        var provider = AIProvider.Create("google-gemini", "Google Gemini", "test");
        provider.SetCredential("ciphertext", "test");
        UseProvider(provider);
        _credentialProtector.Unprotect("ciphertext").Returns("raw-api-key");
    }

    /// <summary>
    /// The provider is no longer found by a hardcoded key — it comes from the
    /// BoundaryVision capability assignment, so a test provider has to be both assigned
    /// and enabled with a default model for the resolver to hand it back.
    /// </summary>
    private void UseProvider(AIProvider provider)
    {
        // Enable() refuses a provider with no credential, so an uncredentialed one stays
        // disabled — which is the real state anyway: the resolver skips a disabled provider
        // and the analyzer degrades to NotConfigured, exactly as the assertions expect.
        if (provider.CredentialCiphertext is not null)
        {
            provider.Enable("test");
        }

        var model = AIModel.Create(
            provider.Id, "gemini-3.6-flash", "Gemini 3.6 Flash", 128000, 8192,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");
        provider.SetDefaultModel(model.Id, "test");

        _providers.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _models.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        _assignments.GetByCapabilityAsync(AiCapability.BoundaryVision, Arg.Any<CancellationToken>())
            .Returns(AiCapabilityAssignment.Create(AiCapability.BoundaryVision, provider.Id, "test"));
    }

    private GeminiBoundaryVisionAnalyzer CreateAnalyzer(Func<HttpRequestMessage, HttpResponseMessage> responder, int visionTimeoutSeconds = 30)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleGemini").Returns(httpClient);

        var options = Options.Create(new GoogleGeminiOptions { ApiKey = "" });
        var boundaryOptions = Options.Create(new BoundaryScoringOptions { VisionTimeoutSeconds = visionTimeoutSeconds });
        var capabilityResolver = new AiCapabilityProviderResolver(
            _assignments, _providers, _models, new DefaultProviderResolver(_providers, _models),
            NullLogger<AiCapabilityProviderResolver>.Instance);
        return new GeminiBoundaryVisionAnalyzer(
            factory, options, boundaryOptions, _providers, _models, capabilityResolver, _credentialProtector,
            NullLogger<GeminiBoundaryVisionAnalyzer>.Instance);
    }

    private static HttpResponseMessage ServiceUnavailable() =>
        new(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"error":{"code":503,"message":"This model is currently experiencing high demand.","status":"UNAVAILABLE"}}""",
                Encoding.UTF8,
                "application/json"),
        };

    /// <summary>
    /// The failure that actually happens in production. On 2026-08-31 every boundary request came
    /// back 503 UNAVAILABLE — "spikes in demand are usually temporary" — and a single attempt
    /// turned each one into a silently uncorrected boundary for hours.
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ShouldRetry_WhenGeminiReportsItIsTemporarilyOverloaded()
    {
        var attempts = 0;
        var analyzer = CreateAnalyzer(_ =>
        {
            attempts++;
            return attempts < 3
                ? ServiceUnavailable()
                : GeminiTextResponse("""{"selected_candidate_id":"osm_1","confidence":0.9,"boundary_quality":"high","reasoning":[],"issues":[],"requires_refinement":false}""");
        });

        var analysis = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, TestContext.Current.CancellationToken);

        attempts.Should().Be(3);
        analysis.AiUsed.Should().BeTrue();
        analysis.SelectedCandidateId.Should().Be("osm_1");
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldGiveUp_AfterTheRetriesAreExhausted()
    {
        var attempts = 0;
        var analyzer = CreateAnalyzer(_ =>
        {
            attempts++;
            return ServiceUnavailable();
        });

        var analysis = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, TestContext.Current.CancellationToken);

        attempts.Should().Be(3, "three attempts including the first, not an unbounded loop");
        analysis.AiUsed.Should().BeFalse();
    }

    /// <summary>
    /// A 4xx other than 429 is our fault — a bad model id, a malformed payload — and will fail
    /// identically however many times it is sent. Retrying it only burns the vision budget.
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ShouldNotRetry_WhenTheRequestItselfWasRejected()
    {
        var attempts = 0;
        var analyzer = CreateAnalyzer(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"code":400,"message":"Invalid model."}}""", Encoding.UTF8, "application/json"),
            };
        });

        var analysis = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, TestContext.Current.CancellationToken);

        attempts.Should().Be(1);
        analysis.AiUsed.Should().BeFalse();
    }

    /// <summary>
    /// Street views arrive as the request's own separate parts, never smuggled into the satellite
    /// image's own coordinate frame — <c>observed_boundary_normalized</c> is only ever meaningful
    /// relative to the satellite image, so the prompt and payload keep them clearly apart.
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ShouldSendEachStreetViewImage_AsItsOwnLabelledPart()
    {
        string? capturedBody = null;
        var analyzer = CreateAnalyzer(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return GeminiTextResponse("""{"selected_candidate_id":"osm_1","confidence":0.9,"boundary_quality":"high","reasoning":[],"issues":[],"requires_refinement":false}""");
        });
        IReadOnlyList<StreetViewImage> streetViews =
        [
            new StreetViewImage([10, 10], "image/jpeg", 25.1550, 55.2218, 0.0),
            new StreetViewImage([20, 20], "image/jpeg", 25.1558, 55.2210, 90.0),
        ];

        await analyzer.AnalyzeAsync(Image, streetViews, Candidates, "Al Safa Park 2", Center, TestContext.Current.CancellationToken);

        capturedBody.Should().NotBeNull();
        using var document = JsonDocument.Parse(capturedBody!);
        var parts = document.RootElement.GetProperty("contents")[0].GetProperty("parts");

        // [0] prompt text, [1] satellite image, [2] street-view intro text, then one
        // (label, image) pair per street view — 2 + 1 + (2 * 2) = 7.
        parts.GetArrayLength().Should().Be(7);

        var promptText = parts[0].GetProperty("text").GetString();
        promptText.Should().Contain("2 ground-level Street View photo(s)");

        var satelliteData = parts[1].GetProperty("inlineData").GetProperty("data").GetString();
        satelliteData.Should().Be(Convert.ToBase64String(Image.ImageBytes));

        var streetViewData = new[] { parts[4], parts[6] }
            .Select(p => p.GetProperty("inlineData").GetProperty("data").GetString())
            .ToList();
        streetViewData.Should().Contain(Convert.ToBase64String(streetViews[0].ImageBytes));
        streetViewData.Should().Contain(Convert.ToBase64String(streetViews[1].ImageBytes));

        var headingLabel = parts[3].GetProperty("text").GetString();
        headingLabel.Should().Contain("heading 0 degrees");
    }

    /// <summary>Regression guard for the payload shape when there is nothing to cross-check with — the pre-existing, no-street-view request must stay exactly as it was.</summary>
    [Fact]
    public async Task AnalyzeAsync_ShouldSendOnlyThePromptAndSatelliteImage_WhenNoStreetViewsAreProvided()
    {
        string? capturedBody = null;
        var analyzer = CreateAnalyzer(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return GeminiTextResponse("""{"selected_candidate_id":"osm_1","confidence":0.9,"boundary_quality":"high","reasoning":[],"issues":[],"requires_refinement":false}""");
        });

        await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(capturedBody!);
        var parts = document.RootElement.GetProperty("contents")[0].GetProperty("parts");
        parts.GetArrayLength().Should().Be(2);
        parts[0].GetProperty("text").GetString().Should().NotContain("Street View");
    }

    private static HttpResponseMessage GeminiTextResponse(string innerJsonText)
    {
        var wrapper = new { candidates = new[] { new { content = new { parts = new[] { new { text = innerJsonText } } } } } };
        var json = System.Text.Json.JsonSerializer.Serialize(wrapper);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldConvertNormalizedCoordinates_ToRealLatLon()
    {
        // (x=0, y=0) is the image's top-left = (West, North); (x=1, y=1) is the bottom-right = (East, South).
        const string innerJson = """
            {
                "selected_candidate_id": "osm_1",
                "confidence": 0.85,
                "boundary_quality": "high",
                "reasoning": ["Matches visible fence line."],
                "issues": [],
                "requires_refinement": false,
                "observed_boundary_normalized": [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
            }
            """;
        var analyzer = CreateAnalyzer(_ => GeminiTextResponse(innerJson));

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeTrue();
        result.SelectedCandidateId.Should().Be("osm_1");
        result.ObservedBoundary.Should().NotBeNull();
        // 5, not the 4 points Gemini reported: the ring is closed automatically (see the
        // dedicated closure tests below), so the first point is repeated as the last.
        result.ObservedBoundary.Should().HaveCount(5);
        result.ObservedBoundary![0].Should().Be(new GeoPoint(Image.North, Image.West));
        result.ObservedBoundary![2].Should().Be(new GeoPoint(Image.South, Image.East));
    }

    /// <summary>
    /// The ring is always explicitly closed (first point repeated as the last), matching every
    /// other polygon ring in this codebase (OSM candidate rings, ConfirmedSiteBoundaryData.Polygon)
    /// — regardless of whether Gemini closed it itself. The prompt asks it not to, but nothing
    /// stops a model doing so anyway or leaving a ring open; both must produce the same guarantee.
    /// </summary>
    [Theory]
    [InlineData("[[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]")]        // open ring, as instructed
    [InlineData("[[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0], [0.0, 0.0]]")] // already closed anyway
    public async Task AnalyzeAsync_ShouldAlwaysReturnAClosedRing_WhetherOrNotGeminiClosedItItself(string observedBoundaryJson)
    {
        var innerJson = $$"""
            {
                "selected_candidate_id": "osm_1",
                "confidence": 0.85,
                "boundary_quality": "high",
                "reasoning": [],
                "issues": [],
                "requires_refinement": false,
                "observed_boundary_normalized": {{observedBoundaryJson}}
            }
            """;
        var analyzer = CreateAnalyzer(_ => GeminiTextResponse(innerJson));

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.ObservedBoundary.Should().NotBeNull();
        result.ObservedBoundary.Should().HaveCount(5, "the ring must be closed exactly once, never left open and never double-closed");
        result.ObservedBoundary![0].Should().Be(result.ObservedBoundary[^1], "the first point must be repeated as the last");
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnNullObservedBoundary_WhenFieldIsAbsent()
    {
        const string innerJson = """
            {
                "selected_candidate_id": "osm_1",
                "confidence": 0.8,
                "boundary_quality": "high",
                "reasoning": [],
                "issues": [],
                "requires_refinement": false,
                "observed_boundary_normalized": null
            }
            """;
        var analyzer = CreateAnalyzer(_ => GeminiTextResponse(innerJson));

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.ObservedBoundary.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnNullObservedBoundary_WhenFewerThanThreePointsAreGiven()
    {
        const string innerJson = """
            {
                "selected_candidate_id": "osm_1",
                "confidence": 0.8,
                "boundary_quality": "medium",
                "reasoning": [],
                "issues": [],
                "requires_refinement": false,
                "observed_boundary_normalized": [[0.1, 0.1], [0.9, 0.9]]
            }
            """;
        var analyzer = CreateAnalyzer(_ => GeminiTextResponse(innerJson));

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.ObservedBoundary.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnNotConfigured_WhenNoCredentialIsConfigured()
    {
        var uncredentialed = AIProvider.Create("google-gemini", "Google Gemini", "test");
        UseProvider(uncredentialed);
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
        result.SelectedCandidateId.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnNotConfigured_WhenHttpRequestFails()
    {
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnNotConfigured_WhenNoCandidatesAreGiven()
    {
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await analyzer.AnalyzeAsync(Image, [], [], "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------
    // specs/043 US5 — Gemini vision is an enhancement and must never become a single point
    // of failure. Every one of these must degrade to the deterministic result, never throw.
    // ---------------------------------------------------------------------------------

    public static TheoryData<HttpStatusCode, string> VisionFailureModes() => new()
    {
        { HttpStatusCode.Unauthorized, """{"error":{"status":"UNAUTHENTICATED"}}""" },
        { HttpStatusCode.Forbidden, """{"error":{"status":"PERMISSION_DENIED","details":[{"reason":"BILLING_DISABLED"}]}}""" },
        { HttpStatusCode.TooManyRequests, """{"error":{"status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure"}]}}""" },
        { HttpStatusCode.TooManyRequests, """{"error":{"status":"RESOURCE_EXHAUSTED"}}""" },
        { HttpStatusCode.InternalServerError, """{"error":{"status":"INTERNAL"}}""" },
        { HttpStatusCode.ServiceUnavailable, "" },
    };

    [Theory]
    [MemberData(nameof(VisionFailureModes))]
    public async Task AnalyzeAsync_ShouldDegradeGracefully_ForEveryProviderFailureMode(HttpStatusCode status, string body)
    {
        // FR-032: quota, rate limit, credential rejection, outage - none may fail the workflow.
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
        result.Reasoning.Should().NotBeEmpty("the workflow must be told why AI verification was unavailable");
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldDegradeGracefully_WhenTheCredentialCannotBeDecrypted()
    {
        _credentialProtector.Unprotect("ciphertext").Returns(_ => throw new System.Security.Cryptography.CryptographicException("key ring changed"));
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldDegradeGracefully_WhenNoCredentialIsConfigured()
    {
        var uncredentialed = AIProvider.Create("google-gemini", "Google Gemini", "test");
        UseProvider(uncredentialed);
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
        result.Reasoning.Should().NotBeEmpty("the workflow must be told why AI verification was unavailable");
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldDegradeGracefully_WhenTheResponseIsUnparseable()
    {
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        });

        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldAbandonTheCall_AtItsOwnBudget_NotTheSharedClientTimeout()
    {
        // specs/043 FR-034/SC-007. The shared "GoogleGemini" client is configured with a
        // two-minute timeout and also serves chat, so the budget has to be scoped here. Before
        // this, a hung vision call stalled an interactive boundary resolution for two minutes
        // before falling back - the fallback was right, just far too late.
        var analyzer = CreateAnalyzer(
            _ =>
            {
                // Outlive the one-second budget configured below.
                Thread.Sleep(TimeSpan.FromMilliseconds(1500));
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            visionTimeoutSeconds: 1);

        var started = DateTime.UtcNow;
        var result = await analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, CancellationToken.None);
        var elapsed = DateTime.UtcNow - started;

        result.AiUsed.Should().BeFalse();
        result.Reasoning.Should().ContainSingle(r => r.Contains("timed out", StringComparison.OrdinalIgnoreCase));
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldPropagateACallerCancellation_RatherThanReportingAProviderFailure()
    {
        // FR-035: the user cancelling is not Gemini failing, and must not be recorded as such.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var act = () => analyzer.AnalyzeAsync(Image, [], Candidates, "Al Safa Park 2", Center, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
