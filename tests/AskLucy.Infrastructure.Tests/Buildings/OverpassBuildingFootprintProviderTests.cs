using System.Net;
using System.Text;
using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Buildings;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Buildings;

/// <summary>
/// specs/052-solar-analysis T034 — <see cref="OverpassBuildingFootprintProvider"/>'s height
/// resolution/provenance (research D5), footprint exclusion counting (FR-013), the count cap
/// (FR-015), the site-building rule (research D8), caching (research D4), and the typed
/// unavailable-exception on every HTTP/network/JSON failure.
/// </summary>
public sealed class OverpassBuildingFootprintProviderTests
{
    private static readonly GeoPoint Center = new(25.1560, 55.2218);

    private static OverpassBuildingFootprintProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IMemoryCache? cache = null,
        BuildingRetrievalOptions? retrievalOptions = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Overpass").Returns(httpClient);

        return new OverpassBuildingFootprintProvider(
            factory,
            Options.Create(new OverpassOptions()),
            Options.Create(retrievalOptions ?? new BuildingRetrievalOptions()),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<OverpassBuildingFootprintProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string BuildingWayJson(long id, string tagsJson, (double Lat, double Lon)[] ring) =>
        $$"""
        {
          "elements": [
            {
              "type": "way",
              "id": {{id}},
              "tags": {{tagsJson}},
              "geometry": [{{string.Join(",", ring.Select(p => $$"""{ "lat": {{p.Lat}}, "lon": {{p.Lon}} }"""))}}]
            }
          ]
        }
        """;

    // A small square around Center, closed ring (first == last).
    private static readonly (double Lat, double Lon)[] SquareRing =
    [
        (25.1560, 55.2210), (25.1560, 55.2220), (25.1550, 55.2220), (25.1550, 55.2210), (25.1560, 55.2210),
    ];

    [Fact]
    public async Task SearchAsync_ShouldResolveHeight_FromHeightTag_AsKnown()
    {
        var provider = CreateProvider(_ => JsonResponse(BuildingWayJson(1, """{ "height": "42.0" }""", SquareRing)));

        var result = await provider.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().ContainSingle();
        result.Buildings[0].HeightMetres.Should().Be(42.0);
        result.Buildings[0].HeightProvenance.Should().Be(BuildingHeightProvenance.Known);
    }

    [Fact]
    public async Task SearchAsync_ShouldParseHeightTag_Leniently_WithTrailingM()
    {
        var provider = CreateProvider(_ => JsonResponse(BuildingWayJson(1, """{ "height": "15 m" }""", SquareRing)));

        var result = await provider.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings[0].HeightMetres.Should().Be(15.0);
        result.Buildings[0].HeightProvenance.Should().Be(BuildingHeightProvenance.Known);
    }

    [Fact]
    public async Task SearchAsync_ShouldResolveHeight_FromLevelsTag_AsAssumed_WhenNoHeightTag()
    {
        var provider = CreateProvider(_ => JsonResponse(BuildingWayJson(2, """{ "building:levels": "4" }""", SquareRing)));

        var result = await provider.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings[0].HeightMetres.Should().Be(12.0); // 4 levels * 3.0m
        result.Buildings[0].HeightProvenance.Should().Be(BuildingHeightProvenance.Assumed);
    }

    [Fact]
    public async Task SearchAsync_ShouldDefaultTo9Metres_AsAssumed_WhenNeitherTagPresent()
    {
        var provider = CreateProvider(_ => JsonResponse(BuildingWayJson(3, "{}", SquareRing)));

        var result = await provider.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings[0].HeightMetres.Should().Be(9.0);
        result.Buildings[0].HeightProvenance.Should().Be(BuildingHeightProvenance.Assumed);
    }

    [Fact]
    public async Task SearchAsync_ShouldExcludeAndCount_UnusableFootprints_WithoutFailingTheWholeAnalysis()
    {
        // One usable closed ring, one open (non-closed) ring, which must be excluded and counted.
        var json = """
            {
              "elements": [
                {
                  "type": "way", "id": 1, "tags": {},
                  "geometry": [
                    { "lat": 25.1560, "lon": 55.2210 }, { "lat": 25.1560, "lon": 55.2220 },
                    { "lat": 25.1550, "lon": 55.2220 }, { "lat": 25.1550, "lon": 55.2210 },
                    { "lat": 25.1560, "lon": 55.2210 }
                  ]
                },
                {
                  "type": "way", "id": 2, "tags": {},
                  "geometry": [
                    { "lat": 25.1500, "lon": 55.2100 }, { "lat": 25.1500, "lon": 55.2110 },
                    { "lat": 25.1490, "lon": 55.2115 }, { "lat": 25.1480, "lon": 55.2120 }
                  ]
                }
              ]
            }
            """;
        var provider = CreateProvider(_ => JsonResponse(json));

        var result = await provider.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().HaveCount(1);
        result.ExcludedCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_ShouldSetLimitedTrue_WhenCountExceedsCap()
    {
        // Five well-separated, individually-closed square rings so none is excluded as unusable.
        static (double Lat, double Lon)[] RingAt(double latOffset) =>
        [
            (25.1560 + latOffset, 55.2210), (25.1560 + latOffset, 55.2220),
            (25.1550 + latOffset, 55.2220), (25.1550 + latOffset, 55.2210), (25.1560 + latOffset, 55.2210),
        ];

        var elementsJson = string.Join(",", Enumerable.Range(1, 5).Select(i =>
            $$"""{ "type": "way", "id": {{i}}, "tags": {}, "geometry": [{{string.Join(",", RingAt(i * 0.01).Select(p => $$"""{ "lat": {{p.Lat}}, "lon": {{p.Lon}} }"""))}}] }"""));
        var json = $$"""{ "elements": [{{elementsJson}}] }""";

        var provider = CreateProvider(_ => JsonResponse(json), retrievalOptions: new BuildingRetrievalOptions { MaxBuildingCount = 2 });

        var result = await provider.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().HaveCount(2);
        result.Limited.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_ShouldMarkTheContainingBuilding_AsTheSiteBuilding()
    {
        // Center sits inside SquareRing (lat 25.1560..25.1550, lon 55.2210..55.2220); Center is
        // (25.1560, 55.2218), just inside the ring's bounds.
        var siteCenter = new GeoPoint(25.1555, 55.2215);
        var provider = CreateProvider(_ => JsonResponse(BuildingWayJson(1, "{}", SquareRing)));

        var result = await provider.SearchAsync(siteCenter, 200, CancellationToken.None);

        result.Buildings.Should().ContainSingle(b => b.IsSiteBuilding);
    }

    [Fact]
    public async Task SearchAsync_ShouldMarkNoBuilding_AsSiteBuilding_WhenNoneContainOrAreNear()
    {
        var farAwayCenter = new GeoPoint(26.5, 56.5); // far from SquareRing
        var provider = CreateProvider(_ => JsonResponse(BuildingWayJson(1, "{}", SquareRing)));

        var result = await provider.SearchAsync(farAwayCenter, 200, CancellationToken.None);

        result.Buildings.Should().NotContain(b => b.IsSiteBuilding);
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptySuccess_WhenNoElements()
    {
        var provider = CreateProvider(_ => JsonResponse("""{"elements":[]}"""));

        var result = await provider.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().BeEmpty();
        result.Limited.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_ShouldHitTheCache_OnASecondCallForTheSameCoordinatesAndRadius()
    {
        var callCount = 0;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(_ =>
        {
            callCount++;
            return JsonResponse(BuildingWayJson(1, "{}", SquareRing));
        }, cache: cache);

        await provider.SearchAsync(Center, 200, CancellationToken.None);
        await provider.SearchAsync(Center, 200, CancellationToken.None);

        callCount.Should().Be(1, "the second call within the cache TTL must not re-query Overpass");
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowBuildingProviderUnavailableException_OnHttpError()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = async () => await provider.SearchAsync(Center, 200, CancellationToken.None);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowBuildingProviderUnavailableException_OnTimeout()
    {
        var provider = CreateProvider(_ => throw new TaskCanceledException("Timed out"));

        var act = async () => await provider.SearchAsync(Center, 200, CancellationToken.None);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldFallOverToAMirror_WhenThePrimaryIsSaturated()
    {
        var hosts = new List<string>();
        var provider = CreateProvider(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return hosts.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
                : JsonResponse(BuildingWayJson(1, "{}", SquareRing));
        });

        var result = await provider.SearchAsync(Center, 200, CancellationToken.None);

        result.Buildings.Should().NotBeEmpty();
        hosts.Should().HaveCount(2);
        hosts[1].Should().NotBe(hosts[0]);
    }
}
