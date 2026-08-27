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

    private GeminiBoundaryVisionAnalyzer CreateAnalyzer(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleGemini").Returns(httpClient);

        var options = Options.Create(new GoogleGeminiOptions { ApiKey = "" });
        return new GeminiBoundaryVisionAnalyzer(factory, options, _providers, _credentialProtector, NullLogger<GeminiBoundaryVisionAnalyzer>.Instance);
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
}
