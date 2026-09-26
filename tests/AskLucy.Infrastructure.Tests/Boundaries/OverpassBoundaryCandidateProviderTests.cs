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

    /// <summary>
    /// The Dubai Mall (relation 18195959, <c>shop=mall</c>) and BurJuman (way 225672808,
    /// <c>shop=mall</c>) were never candidates on 2026-09-25: no filter matched malls, and
    /// relations were not queried at all.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldQueryMalls_AndMultipolygonRelations_ForCuratedFiltersOnly()
    {
        string? capturedQuery = null;
        var provider = CreateProvider(request =>
        {
            capturedQuery = WebUtility.UrlDecode(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return JsonResponse("""{"elements":[]}""");
        }, out _);

        await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        capturedQuery.Should().Contain("""way(around:500,25.156,55.2218)["shop"="mall"];""");
        capturedQuery.Should().Contain("""relation(around:500,25.156,55.2218)["type"="multipolygon"]["shop"="mall"];""");
        capturedQuery.Should().Contain("""relation(around:500,25.156,55.2218)["type"="multipolygon"]["leisure"="park"];""");
        capturedQuery.Should().NotContain("""relation(around:500,25.156,55.2218)["type"="multipolygon"]["landuse"]""",
            "an any-value landuse relation query drags in district-scale areas nobody names as a site");
    }

    /// <summary>
    /// Muscat, 2026-09-26: the Royal Opera House is <c>amenity=theatre</c> and Sultan Qaboos Grand
    /// Mosque a <c>building=mosque</c> relation. Neither was queried.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldQueryTheatresAndPlacesOfWorship_ButOnlyNamedLandmarkBuildingValues()
    {
        string? capturedQuery = null;
        var provider = CreateProvider(request =>
        {
            capturedQuery = WebUtility.UrlDecode(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return JsonResponse("""{"elements":[]}""");
        }, out _);

        await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        capturedQuery.Should().Contain("""way(around:500,25.156,55.2218)["amenity"="theatre"];""");
        capturedQuery.Should().Contain("""way(around:500,25.156,55.2218)["amenity"="place_of_worship"];""");
        capturedQuery.Should().Contain("""relation(around:500,25.156,55.2218)["type"="multipolygon"]["building"="mosque"];""");
        capturedQuery.Should().NotContain("""way(around:500,25.156,55.2218)["building"];""",
            "every building in the radius is far too much to download");
    }

    [Fact]
    public async Task SearchAsync_ShouldAssembleARelationsOuterRing_FromOutOfOrderReversedMemberWays()
    {
        // Two outer halves of a square, the second stored in the opposite direction, plus an
        // inner hole that must not become part of the outline.
        var relationJson = """
            {
              "elements": [
                {
                  "type": "relation",
                  "id": 18195959,
                  "tags": { "type": "multipolygon", "shop": "mall", "name": "The Dubai Mall" },
                  "members": [
                    {
                      "type": "way", "ref": 1, "role": "outer",
                      "geometry": [
                        { "lat": 25.2000, "lon": 55.2700 },
                        { "lat": 25.2000, "lon": 55.2800 },
                        { "lat": 25.1940, "lon": 55.2800 }
                      ]
                    },
                    {
                      "type": "way", "ref": 3, "role": "inner",
                      "geometry": [
                        { "lat": 25.1980, "lon": 55.2740 },
                        { "lat": 25.1980, "lon": 55.2760 },
                        { "lat": 25.1960, "lon": 55.2760 },
                        { "lat": 25.1980, "lon": 55.2740 }
                      ]
                    },
                    {
                      "type": "way", "ref": 2, "role": "outer",
                      "geometry": [
                        { "lat": 25.2000, "lon": 55.2700 },
                        { "lat": 25.1940, "lon": 55.2700 },
                        { "lat": 25.1940, "lon": 55.2800 }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        var provider = CreateProvider(_ => JsonResponse(relationJson), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Id.Should().Be("osm_relation_18195959");
        results[0].Name.Should().Be("The Dubai Mall");
        results[0].Tags.Should().ContainKey("shop").WhoseValue.Should().Be("mall");
        results[0].Polygon.ExteriorRing.Should().Equal(
            new GeoPoint(25.2000, 55.2700), new GeoPoint(25.2000, 55.2800), new GeoPoint(25.1940, 55.2800),
            new GeoPoint(25.1940, 55.2700), new GeoPoint(25.2000, 55.2700));
        results[0].AreaSquareMeters.Should().BeApproximately(667 * 1007, 667 * 1007 * 0.02);
    }

    [Fact]
    public async Task SearchAsync_ShouldSkipARelation_WhoseOuterWaysDoNotClose()
    {
        // A member missing from the response (e.g. an incomplete download) leaves a gap; closing
        // it with a straight line would invent an edge.
        var brokenRelationJson = """
            {
              "elements": [
                {
                  "type": "relation",
                  "id": 42,
                  "tags": { "type": "multipolygon", "shop": "mall" },
                  "members": [
                    {
                      "type": "way", "ref": 1, "role": "outer",
                      "geometry": [
                        { "lat": 25.2000, "lon": 55.2700 },
                        { "lat": 25.2000, "lon": 55.2800 },
                        { "lat": 25.1940, "lon": 55.2800 }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        var provider = CreateProvider(_ => JsonResponse(brokenRelationJson), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, CancellationToken.None);

        results.Should().BeEmpty();
    }

    /// <summary>
    /// Two closed ways side by side: west spans lon 55.300-55.301, east 55.301-55.302. When
    /// <paramref name="shareWall"/> they share the whole wall at lon 55.301 (both of its nodes);
    /// otherwise they meet only at its top corner.
    /// </summary>
    private static string TwoAdjacentWaysJson(string westTags, string eastTags, bool shareWall = true)
    {
        var eastSouthWest = shareWall ? "{ \"lat\": 25.2530, \"lon\": 55.3010 }" : "{ \"lat\": 25.2530, \"lon\": 55.3015 }";
        return $$"""
            {
              "elements": [
                {
                  "type": "way", "id": 1, "tags": {{westTags}},
                  "geometry": [
                    { "lat": 25.2540, "lon": 55.3000 }, { "lat": 25.2540, "lon": 55.3010 },
                    { "lat": 25.2530, "lon": 55.3010 }, { "lat": 25.2530, "lon": 55.3000 },
                    { "lat": 25.2540, "lon": 55.3000 }
                  ]
                },
                {
                  "type": "way", "id": 2, "tags": {{eastTags}},
                  "geometry": [
                    { "lat": 25.2540, "lon": 55.3010 }, { "lat": 25.2540, "lon": 55.3020 },
                    { "lat": 25.2530, "lon": 55.3020 }, {{eastSouthWest}},
                    { "lat": 25.2540, "lon": 55.3010 }
                  ]
                }
              ]
            }
            """;
    }

    /// <summary>
    /// BurJuman, 2026-09-25: OSM maps it as two <c>shop=mall</c> ways that share a wall, and only
    /// one half was highlighted.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldMergeAdjacentPartsOfOneSite_IntoOneOutline()
    {
        var json = TwoAdjacentWaysJson(
            """{ "shop": "mall", "name": "مركز برجمان للتسوق", "name:en": "BurJuman Mall" }""",
            """{ "shop": "mall", "name": "مركز برجمان دبي", "name:en": "Bur Juman Shopping Center" }""");
        var provider = CreateProvider(_ => JsonResponse(json), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
        results[0].Id.Should().Be("osm_way_1+osm_way_2");
        results[0].Polygon.ExteriorRing.Should().HaveCount(7, "six corners plus the closing point; the shared wall is gone");
        results[0].Polygon.ExteriorRing[0].Should().Be(results[0].Polygon.ExteriorRing[^1]);
        var halfArea = GeometryMath.AreaSquareMeters([
            new(25.2540, 55.3000), new(25.2540, 55.3010), new(25.2530, 55.3010), new(25.2530, 55.3000), new(25.2540, 55.3000)]);
        results[0].AreaSquareMeters.Should().BeApproximately(2 * halfArea, 2 * halfArea * 0.01);
    }

    /// <summary>
    /// The merged site used to keep only the first part's tags. On BurJuman that was the part
    /// named "Bur Juman Shopping Center", so the other part's "BurJuman Mall" was lost and the
    /// mall scored no name match against the user's "BurJuman Mall".
    /// </summary>
    [Fact]
    public async Task SearchAsync_ShouldKeepEveryPartsName_WhenMergingPartsOfOneSite()
    {
        var json = TwoAdjacentWaysJson(
            """{ "shop": "mall", "name": "Bur Juman Shopping Center", "name:en": "Bur Juman Shopping Center" }""",
            """{ "shop": "mall", "name": "BurJuman", "name:en": "BurJuman Mall", "building": "retail" }""");
        var provider = CreateProvider(_ => JsonResponse(json), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Bur Juman Shopping Center");
        results[0].Tags["name:en"].Should().Be("Bur Juman Shopping Center");
        results[0].Tags["alt_name"].Split(';').Should().BeEquivalentTo("BurJuman", "BurJuman Mall");
        results[0].Tags["building"].Should().Be("retail", "tags only the second part carries are kept too");
    }

    [Fact]
    public async Task SearchAsync_ShouldKeepAdjacentSitesApart_WhenTheirNamesDiffer()
    {
        var json = TwoAdjacentWaysJson(
            """{ "leisure": "park", "name": "Al Safa Park 1" }""",
            """{ "leisure": "park", "name": "Al Safa Park 2" }""");
        var provider = CreateProvider(_ => JsonResponse(json), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_ShouldKeepAdjacentSitesApart_WhenTheyAreDifferentKindsOfSite()
    {
        var json = TwoAdjacentWaysJson(
            """{ "shop": "mall", "name": "BurJuman Mall" }""",
            """{ "leisure": "park", "name": "BurJuman Park" }""");
        var provider = CreateProvider(_ => JsonResponse(json), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_ShouldNotMerge_PartsThatOnlyTouchAtACorner()
    {
        var json = TwoAdjacentWaysJson(
            """{ "shop": "mall", "name": "BurJuman Mall" }""",
            """{ "shop": "mall", "name": "Bur Juman Shopping Center" }""",
            shareWall: false);
        var provider = CreateProvider(_ => JsonResponse(json), out _);

        var results = await provider.SearchAsync(AlSafaCenter, 500, TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
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
