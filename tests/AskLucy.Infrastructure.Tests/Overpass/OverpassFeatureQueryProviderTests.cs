using System.Net;
using System.Text;
using AskLucy.Application.Overpass;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Overpass;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Overpass;

/// <summary>
/// specs/089-site-geometry-specialist contracts/overpass-feature-query.md — the shared Overpass read
/// path: query construction from filters, element interpretation (open ways, closed ways, relations),
/// and the endpoint hedge the boundary search already relied on.
/// </summary>
public sealed class OverpassFeatureQueryProviderTests
{
    private static readonly GeoPoint Center = new(25.1560, 55.2210);

    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private static OverpassFeatureQueryProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        return CreateProvider(handler);
    }

    private static OverpassFeatureQueryProvider CreateProvider(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Overpass").Returns(httpClient);
        return new OverpassFeatureQueryProvider(
            factory, Options.Create(new OverpassOptions()), NullLogger<OverpassFeatureQueryProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private const string ClosedWayJson = """
        {
          "elements": [
            {
              "type": "way",
              "id": 42,
              "tags": { "boundary": "parcel", "name": "Plot 7" },
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

    private const string OpenWayJson = """
        {
          "elements": [
            {
              "type": "way",
              "id": 77,
              "tags": { "highway": "residential", "name": "Test Street" },
              "geometry": [
                { "lat": 25.1500, "lon": 55.2200 },
                { "lat": 25.1510, "lon": 55.2200 },
                { "lat": 25.1520, "lon": 55.2200 }
              ]
            }
          ]
        }
        """;

    /// <summary>
    /// One relation split across two out-of-order, oppositely wound member ways describing the square
    /// A-B-C-D: the first runs C→D→A, the second runs C→B→A (the first way reversed).
    /// </summary>
    private const string RelationJson = """
        {
          "elements": [
            {
              "type": "relation",
              "id": 18195959,
              "tags": { "boundary": "parcel", "name": "Split Plot" },
              "members": [
                {
                  "type": "way",
                  "role": "outer",
                  "geometry": [
                    { "lat": 25.1560, "lon": 55.2220 },
                    { "lat": 25.1550, "lon": 55.2220 },
                    { "lat": 25.1550, "lon": 55.2210 }
                  ]
                },
                {
                  "type": "way",
                  "role": "outer",
                  "geometry": [
                    { "lat": 25.1560, "lon": 55.2220 },
                    { "lat": 25.1560, "lon": 55.2210 },
                    { "lat": 25.1550, "lon": 55.2210 }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private const string BrokenRelationJson = """
        {
          "elements": [
            {
              "type": "relation",
              "id": 5,
              "tags": { "boundary": "parcel" },
              "members": [
                {
                  "type": "way",
                  "role": "outer",
                  "geometry": [
                    { "lat": 25.1550, "lon": 55.2210 },
                    { "lat": 25.1560, "lon": 55.2210 }
                  ]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void BuildQuery_ShouldEmitAWayAndRelationPerValue_WhenRelationsAreIncluded()
    {
        var query = new OverpassFeatureQuery(Center, 500,
            [new OverpassTagFilter("boundary", ["parcel", "cadastral"], IncludeRelations: true)]);

        var ql = OverpassFeatureQueryProvider.BuildQuery(query);

        ql.Should().StartWith("[out:json][timeout:25];(");
        ql.Should().Contain("way(around:500,25.156,55.221)[\"boundary\"=\"parcel\"];");
        ql.Should().Contain("way(around:500,25.156,55.221)[\"boundary\"=\"cadastral\"];");
        ql.Should().Contain("relation(around:500,25.156,55.221)[\"type\"=\"multipolygon\"][\"boundary\"=\"parcel\"];");
        ql.Should().Contain("relation(around:500,25.156,55.221)[\"type\"=\"multipolygon\"][\"boundary\"=\"cadastral\"];");
        ql.Should().EndWith(");out geom;");
    }

    [Fact]
    public void BuildQuery_ShouldEmitOnlyAWay_ForAnAnyValueFilter()
    {
        var query = new OverpassFeatureQuery(Center, 120, [new OverpassTagFilter("highway", null)]);

        var ql = OverpassFeatureQueryProvider.BuildQuery(query);

        ql.Should().Contain("way(around:120,25.156,55.221)[\"highway\"];");
        ql.Should().NotContain("relation");
    }

    [Fact]
    public void Interpret_ShouldReturnAClosedWayExactlyAsMapped()
    {
        var features = OverpassFeatureQueryProvider.Interpret(Parse(ClosedWayJson));

        features.Should().ContainSingle();
        var parcel = features[0];
        parcel.Id.Should().Be("osm_way_42");
        parcel.ElementType.Should().Be("way");
        parcel.Tags["boundary"].Should().Be("parcel");
        parcel.Geometry.Should().HaveCount(5);
        parcel.Geometry[0].Should().Be(parcel.Geometry[^1]);
    }

    [Fact]
    public void Interpret_ShouldKeepAnOpenWayAsMapped()
    {
        var features = OverpassFeatureQueryProvider.Interpret(Parse(OpenWayJson));

        features.Should().ContainSingle();
        features[0].Geometry.Should().HaveCount(3, "a street is a path, not a ring");
        features[0].Tags["name"].Should().Be("Test Street");
    }

    [Fact]
    public void Interpret_ShouldAssembleARelationsOuterRing_FromOutOfOrderWays()
    {
        var features = OverpassFeatureQueryProvider.Interpret(Parse(RelationJson));

        features.Should().ContainSingle();
        var relation = features[0];
        relation.Id.Should().Be("osm_relation_18195959");
        relation.ElementType.Should().Be("relation");
        relation.Geometry.Should().HaveCount(5);
        relation.Geometry[0].Should().Be(relation.Geometry[^1]);
    }

    [Fact]
    public void Interpret_ShouldOmitARelationWhoseOuterWaysDoNotClose()
    {
        var features = OverpassFeatureQueryProvider.Interpret(Parse(BrokenRelationJson));

        features.Should().BeEmpty("a chain that cannot close is dropped, never force-closed");
    }

    [Fact]
    public async Task QueryAsync_ShouldFallOverToAMirror_WhenThePrimaryIsSaturated()
    {
        var hosts = new List<string>();
        var provider = CreateProvider(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return hosts.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
                : JsonResponse(ClosedWayJson);
        }, out _);

        var features = await provider.QueryAsync(
            new OverpassFeatureQuery(Center, 500, [new OverpassTagFilter("boundary", ["parcel"])]),
            TestContext.Current.CancellationToken);

        features.Should().ContainSingle();
        hosts.Should().HaveCount(2);
        hosts[1].Should().NotBe(hosts[0], "a saturated host must not be retried in place");
    }

    [Fact]
    public async Task QueryAsync_ShouldThrowUnavailable_WhenEveryEndpointFails()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), out _);

        var act = () => provider.QueryAsync(
            new OverpassFeatureQuery(Center, 500, [new OverpassTagFilter("boundary", ["parcel"])]),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<BoundaryProviderUnavailableException>();
    }

    private static IReadOnlyList<OverpassFeatureQueryProvider.OverpassElement> Parse(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var elements = System.Text.Json.JsonSerializer.Deserialize<OverpassFeatureQueryProvider.OverpassResponse>(
            document.RootElement.GetRawText(), SerializerOptions);

        return elements?.Elements ?? [];
    }
}
