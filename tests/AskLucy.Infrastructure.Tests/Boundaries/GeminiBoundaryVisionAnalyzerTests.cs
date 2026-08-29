using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
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
        _providers.GetByKeyAsync("google-gemini", Arg.Any<CancellationToken>()).Returns(provider);
        _credentialProtector.Unprotect("ciphertext").Returns("raw-api-key");
    }

    private GeminiBoundaryVisionAnalyzer CreateAnalyzer(Func<HttpRequestMessage, HttpResponseMessage> responder, int visionTimeoutSeconds = 30)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleGemini").Returns(httpClient);

        var options = Options.Create(new GoogleGeminiOptions { ApiKey = "" });
        var boundaryOptions = Options.Create(new BoundaryScoringOptions { VisionTimeoutSeconds = visionTimeoutSeconds });
        return new GeminiBoundaryVisionAnalyzer(factory, options, boundaryOptions, _providers, _credentialProtector, NullLogger<GeminiBoundaryVisionAnalyzer>.Instance);
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

        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeTrue();
        result.SelectedCandidateId.Should().Be("osm_1");
        result.ObservedBoundary.Should().NotBeNull();
        result.ObservedBoundary.Should().HaveCount(4);
        result.ObservedBoundary![0].Should().Be(new GeoPoint(Image.North, Image.West));
        result.ObservedBoundary![2].Should().Be(new GeoPoint(Image.South, Image.East));
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

        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);

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

        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.ObservedBoundary.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnNotConfigured_WhenNoCredentialIsConfigured()
    {
        var uncredentialed = AIProvider.Create("google-gemini", "Google Gemini", "test");
        _providers.GetByKeyAsync("google-gemini", Arg.Any<CancellationToken>()).Returns(uncredentialed);
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
        result.SelectedCandidateId.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnNotConfigured_WhenHttpRequestFails()
    {
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnNotConfigured_WhenNoCandidatesAreGiven()
    {
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await analyzer.AnalyzeAsync(Image, [], "Al Safa Park 2", Center, CancellationToken.None);

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

        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
        result.Reasoning.Should().NotBeEmpty("the workflow must be told why AI verification was unavailable");
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldDegradeGracefully_WhenTheCredentialCannotBeDecrypted()
    {
        _credentialProtector.Unprotect("ciphertext").Returns(_ => throw new System.Security.Cryptography.CryptographicException("key ring changed"));
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);

        result.AiUsed.Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldDegradeGracefully_WhenNoCredentialIsConfigured()
    {
        var uncredentialed = AIProvider.Create("google-gemini", "Google Gemini", "test");
        _providers.GetByKeyAsync("google-gemini", Arg.Any<CancellationToken>()).Returns(uncredentialed);
        var analyzer = CreateAnalyzer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);

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

        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);

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
        var result = await analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, CancellationToken.None);
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

        var act = () => analyzer.AnalyzeAsync(Image, Candidates, "Al Safa Park 2", Center, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
