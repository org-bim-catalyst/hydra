using System.Net;
using System.Text;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>
/// specs/042-site-boundary-resolution T022/T039 — <see cref="OverpassBoundaryCandidateProvider"/>
/// parses Overpass JSON <c>way</c> elements into <see cref="BoundaryCandidate"/> records, skips
/// non-closed ways, and maps all HTTP/network/JSON failures to
/// <see cref="BoundaryProviderUnavailableException"/> (Scenario E — data source unavailable).
/// </summary>
public sealed class OverpassBoundaryCandidateProviderTests
{
    private const string ClosedParkWayJson = """
        {
          "elements": [
            {
              "type": "way",
              "id": 123456,
              "tags": { "leisure": "park", "name": "Al Safa Park 2" },
              "geometry": [
                { "lat": 25.1560, "lon": 55.2210 },
                { "lat": 25.1560, "lon": 55.2220 },
                { "lat": 25.1550, "lon": 55.2220 },
                { "lat": 25.1550, "lon": 55.2210 },
                { "lat": 25.1560, "lon": 55.2210 }
              ]
            }
          ]
        }
        """;

    private static OverpassBoundaryCandidateProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Overpass").Returns(httpClient);
        var options = Options.Create(new OverpassOptions());
        return new OverpassBoundaryCandidateProvider(factory, options, NullLogger<OverpassBoundaryCandidateProvider>.Instance);
    }

    /// <summary>
    /// The saturated host is the load balancer, not the data.
    ///
    /// <para>
    /// On 2026-08-31 <c>overpass-api.de</c> answered 504 three times ~10 s apart and the site was
    /// left with no boundary at all; later it returned 429 on every attempt. Its cluster's own
    /// nodes answered the same query in 1-5 s throughout. Retrying the same entry point could
    /// never have recovered that.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldFallOverToAMirror_WhenThePrimaryIsSaturated()
    {
        var hosts = new List<string>();
        var provider = CreateProvider(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return hosts.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
                : JsonResponse(ClosedParkWayJson);
        }, out _);

        var candidates = await provider.SearchAsync(AlSafaCenter, 500, TestContext.Current.CancellationToken);

        candidates.Should().NotBeEmpty("the mirror answered what the primary could not");
        hosts.Should().HaveCount(2);
        hosts[1].Should().NotBe(hosts[0], "the second attempt must go to a different host, not the same saturated one");
    }

    [Fact]
    public async Task SearchAsync_ShouldGiveUp_OnlyAfterEveryEndpointHasBeenTried()
    {
        var hosts = new List<string>();
        var provider = CreateProvider(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        }, out _);

        var act = async () => await provider.SearchAsync(AlSafaCenter, 500, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<BoundaryProviderUnavailableException>();
        hosts.Distinct().Should().HaveCountGreaterThan(1, "giving up must not mean hammering one host three times");
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly GeoPoint AlSafaCenter = new(25.1560, 55.2218);

    [Fact]
    public async Task SearchAsync_ShouldReturnMappedCandidate_ForAClosedWay()
    {
        // Captured inside the responder — the provider disposes its request (and content) via
        // `using` immediately after sending, so reading the body after SearchAsync returns
        // would hit a disposed FormUrlEncodedContent.
        string? capturedBody = null;
        var provider = CreateProvider(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(ClosedParkWayJson);
        }, out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Id.Should().Be("osm_way_123456");
        results[0].Name.Should().Be("Al Safa Park 2");
        results[0].Source.Should().Be(SiteBoundarySource.OsmBoundary);
        results[0].Tags.Should().ContainKey("leisure").WhoseValue.Should().Be("park");
        results[0].Polygon.ExteriorRing.Should().HaveCount(5);
        results[0].AreaSquareMeters.Should().BeGreaterThan(0);

        capturedBody.Should().Contain("around%3A500");
    }

    [Fact]
    public async Task SearchAsync_ShouldSkipWays_WhenGeometryIsNotAClosedRing()
    {
        var openWayJson = """
            {
              "elements": [
                {
                  "type": "way",
                  "id": 999,
                  "tags": { "highway": "footway" },
                  "geometry": [
                    { "lat": 25.1560, "lon": 55.2210 },
                    { "lat": 25.1560, "lon": 55.2220 },
                    { "lat": 25.1550, "lon": 55.2225 },
                    { "lat": 25.1545, "lon": 55.2230 }
                  ]
                }
              ]
            }
            """;
        var provider = CreateProvider(_ => JsonResponse(openWayJson), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmpty_WhenOverpassReturnsNoElements()
    {
        var provider = CreateProvider(_ => JsonResponse("""{"elements":[]}"""), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowBoundaryProviderUnavailableException_OnHttpError()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), out _);

        var act = async () => await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        await act.Should().ThrowAsync<BoundaryProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowBoundaryProviderUnavailableException_OnTimeout()
    {
        var provider = CreateProvider(_ => throw new TaskCanceledException("Timed out"), out _);

        var act = async () => await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        await act.Should().ThrowAsync<BoundaryProviderUnavailableException>();
    }

    // specs/042-site-boundary-resolution T044 (US4) — the same parsing/mapping logic applies to
    // any OSM tag family (building, amenity), not just leisure=park, proving no site-specific
    // hardcoding in the candidate provider.
    [Theory]
    [InlineData("amenity", "school", "Dubai American Academy")]
    [InlineData("building", "residential", "221B Baker Street")]
    public async Task SearchAsync_ShouldMapAnyTaggedClosedWay_RegardlessOfSiteType(string tagKey, string tagValue, string siteName)
    {
        var json = $$"""
            {
              "elements": [
                {
                  "type": "way",
                  "id": 777,
                  "tags": { "{{tagKey}}": "{{tagValue}}", "name": "{{siteName}}" },
                  "geometry": [
                    { "lat": 25.1560, "lon": 55.2210 },
                    { "lat": 25.1560, "lon": 55.2220 },
                    { "lat": 25.1550, "lon": 55.2220 },
                    { "lat": 25.1550, "lon": 55.2210 },
                    { "lat": 25.1560, "lon": 55.2210 }
                  ]
                }
              ]
            }
            """;
        var provider = CreateProvider(_ => JsonResponse(json), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        results.Should().ContainSingle(c => c.Name == siteName && c.Tags[tagKey] == tagValue);
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowBoundaryProviderUnavailableException_OnMalformedJson()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        }, out _);

        var act = async () => await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        await act.Should().ThrowAsync<BoundaryProviderUnavailableException>();
    }
}
