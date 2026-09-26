using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Buildings;
using AskLucy.Infrastructure.Buildings.Esri;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Buildings.Esri;

/// <summary>
/// specs/075 — <see cref="EsriBuildingHeightSource"/> against a synthetic I3S scene layer: the
/// tree walk (and what it prunes), the measured-vs-placeholder height rule, the radius filter,
/// gzip handling, the optional key header, caching and the typed unavailable-exception. Geometry
/// decoding is faked here; its pure parts are covered by <see cref="I3sPrimitivesTests"/>.
/// </summary>
public sealed class EsriBuildingHeightSourceTests
{
    private const string LayerUrl = "https://scene.test/layers/0";
    private static readonly GeoPoint Center = new(25.1560, 55.2218);
    private static readonly GeoPoint AbuDhabi = new(24.4539, 54.3773);

    private sealed class Layer
    {
        public List<string> Requests { get; } = [];

        public HttpStatusCode? FailWith { get; set; }

        public bool GzipBodies { get; set; }

        public float[] Heights { get; set; } = [25f, 3f, 3f, 12f];

        public string[] Sources { get; set; } = ["Vantor", "OSM", "Vantor", "OSM"];

        /// <summary>When set, every leaf-data request answers HTTP 200 with this ArcGIS error document.</summary>
        public string? LeafErrorBody { get; set; }

        /// <summary>Answer <see cref="LeafErrorBody"/> only to requests that carry a key.</summary>
        public bool LeafErrorOnlyWithKey { get; set; }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests) Requests.Add(url);
            if (FailWith is { } status) return new HttpResponseMessage(status);
            if (LeafErrorBody is { } error && url.Contains("/nodes/")
                && (!LeafErrorOnlyWithKey || request.Headers.Contains("X-Esri-Authorization")))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(error) };
            }

            var path = url[LayerUrl.Length..];
            byte[] body = path switch
            {
                "?f=json" => Encoding.UTF8.GetBytes("""
                    { "version": "v7", "nodePages": { "nodesPerPage": 2 },
                      "attributeStorageInfo": [
                        { "key": "f_0", "name": "height", "attributeValues": { "valueType": "Float32" } },
                        { "key": "f_3", "name": "OSMID", "attributeValues": { "valueType": "Float64" } },
                        { "key": "f_8", "name": "source", "attributeValues": { "valueType": "String" } } ] }
                    """),
                // Node 0 is the Earth-sized root; node 1 is the site's leaf; node 2 is a leaf in Abu Dhabi.
                "/nodepages/0" => Encoding.UTF8.GetBytes($$"""
                    { "nodes": [
                      { "index": 0, "obb": { "center": [0, 0, -6356752], "halfSize": [6400000, 6400000, 6400000], "quaternion": [0, 0, 0, 1] }, "children": [1, 2] },
                      { "index": 1, "obb": { "center": [{{Center.Longitude}}, {{Center.Latitude}}, 0], "halfSize": [300, 300, 300], "quaternion": [0, 0, 0, 1] },
                        "mesh": { "geometry": { "resource": 10 }, "attribute": { "resource": 10 } } } ] }
                    """),
                "/nodepages/1" => Encoding.UTF8.GetBytes($$"""
                    { "nodes": [
                      { "index": 2, "obb": { "center": [{{AbuDhabi.Longitude}}, {{AbuDhabi.Latitude}}, 0], "halfSize": [300, 300, 300], "quaternion": [0, 0, 0, 1] },
                        "mesh": { "geometry": { "resource": 20 }, "attribute": { "resource": 20 } } } ] }
                    """),
                "/nodes/10/geometries/0" => [1, 2, 3],
                "/nodes/10/attributes/f_0/0" => Float32Attribute(Heights),
                "/nodes/10/attributes/f_8/0" => StringAttribute(Sources),
                _ => [],
            };
            if (body.Length == 0) return new HttpResponseMessage(HttpStatusCode.NotFound);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(GzipBodies ? Gzip(body) : body) };
        }
    }

    /// <summary>Where each feature's flat 12 m square roof is centred.</summary>
    private static readonly GeoPoint[] FeatureCentres =
    [
        Center,
        new(Center.Latitude, Center.Longitude + 0.0002),
        new(Center.Latitude + 0.0002, Center.Longitude),
        new(Center.Latitude + 0.018, Center.Longitude),
    ];

    /// <summary>
    /// Feature 0, 1 and 2 at the site, about 20 m apart; feature 3 two kilometres north, outside a
    /// 200 m search. Each is a flat square roof on ground 5 m above the ellipsoid, at the layer's
    /// height for that feature. Hand-written: NSubstitute cannot proxy an internal interface.
    /// </summary>
    private sealed class FakeDecoder(float[] heights) : II3sGeometryDecoder
    {
        private const double GroundElevation = 5;
        private const double HalfSideDegrees = 6 / 111_320.0;

        public I3sMesh Decode(byte[] geometry, double nodeCenterLongitude, double nodeCenterLatitude)
        {
            var longitudes = new List<double>();
            var latitudes = new List<double>();
            var elevations = new List<float>();
            var features = new List<int>();
            var triangles = new List<int>();
            for (var f = 0; f < FeatureCentres.Length; f++)
            {
                var first = features.Count;
                foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    longitudes.Add(FeatureCentres[f].Longitude + (dx * HalfSideDegrees));
                    latitudes.Add(FeatureCentres[f].Latitude + (dy * HalfSideDegrees));
                    elevations.Add((float)(GroundElevation + heights[f]));
                    features.Add(f);
                }

                triangles.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            }

            return new I3sMesh([.. longitudes], [.. latitudes], [.. elevations], [.. features], [.. triangles]);
        }
    }

    private static float HeightAt(BuildingHeightMap map, GeoPoint point)
    {
        var (x, y) = map.Grid.ToGrid(point.Longitude, point.Latitude);
        return map[(int)x, (int)y];
    }

    private static (EsriBuildingHeightSource Source, Layer Layer, StubHttpMessageHandler Handler) Create(
        Action<Layer>? arrange = null, EsriOptions? esriOptions = null, bool enabled = true)
    {
        var layer = new Layer();
        arrange?.Invoke(layer);
        var handler = new StubHttpMessageHandler(layer.Respond);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(EsriBuildingHeightSource.HttpClientName).Returns(_ => new HttpClient(handler));

        var source = new EsriBuildingHeightSource(
            factory,
            new FakeDecoder(layer.Heights),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new EsriBuildingsOptions { SceneLayerUrl = LayerUrl, Enabled = enabled }),
            Options.Create(esriOptions ?? new EsriOptions()),
            Options.Create(new BuildingRetrievalOptions()),
            NullLogger<EsriBuildingHeightSource>.Instance);
        return (source, layer, handler);
    }

    [Fact]
    public async Task SearchAsync_ShouldMapMeasuredRoofs_AndDropPlaceholders()
    {
        var (source, _, _) = Create();

        var map = await source.SearchAsync(Center, 200);

        HeightAt(map, FeatureCentres[0]).Should().BeApproximately(25, 0.01f, "Vantor's 25 m is a measurement");
        HeightAt(map, FeatureCentres[2]).Should().BeApproximately(3, 0.01f, "so is Vantor's 3 m");
        float.IsNaN(HeightAt(map, FeatureCentres[1])).Should().BeTrue("OSM's 3 m is the layer's no-height placeholder");
        map.Grid.North.Should().BeLessThan(FeatureCentres[3].Latitude, "OSM's 12 m is 2 km away");
        float.IsNaN(HeightAt(map, new GeoPoint(Center.Latitude - 0.0005, Center.Longitude))).Should().BeTrue("no roof covers open ground");
    }

    [Fact]
    public void Rasterize_ShouldMeasureEachRoofFromItsOwnFeaturesGround()
    {
        // Two features on different ground: a 10 m roof at elevation 110 on 100 m ground, and a
        // 30 m roof whose tallest vertex (elevation 50) is its height above 20 m ground.
        var grid = GeoGrid.Around(Center, 20, 2);
        var d = 4 / 111_320.0;
        var mesh = new I3sMesh(
            [Center.Longitude - (3 * d), Center.Longitude - d, Center.Longitude - d, Center.Longitude + d, Center.Longitude + (3 * d), Center.Longitude + (3 * d)],
            [Center.Latitude - d, Center.Latitude - d, Center.Latitude + d, Center.Latitude - d, Center.Latitude - d, Center.Latitude + d],
            [110, 110, 110, 40, 50, 50],
            [0, 0, 0, 1, 1, 1],
            [0, 1, 2, 3, 4, 5]);
        var heights = new float[grid.Width * grid.Height];
        Array.Fill(heights, float.NaN);

        RoofHeightRasterizer.Rasterize(mesh, [10, 30], [true, true], grid, heights);
        var map = new BuildingHeightMap(grid, heights);

        HeightAt(map, new GeoPoint(Center.Latitude - (0.6 * d), Center.Longitude - (1.4 * d))).Should().BeApproximately(10, 0.01f);
        HeightAt(map, new GeoPoint(Center.Latitude - (0.6 * d), Center.Longitude + (2.6 * d))).Should().BeInRange(20, 30, "the roof slopes from 20 m up to 30 m");
    }

    [Fact]
    public async Task SearchAsync_ShouldNotReadNodesWhoseBoxIsOutOfRange()
    {
        var (source, layer, _) = Create();

        await source.SearchAsync(Center, 200);

        layer.Requests.Should().Contain(r => r.EndsWith("/nodepages/1"), "the far node's box is only known once its page is read");
        layer.Requests.Should().NotContain(r => r.Contains("/nodes/20/"), "a leaf 130 km away must never be downloaded");
    }

    [Fact]
    public async Task SearchAsync_ShouldReadGzippedBodies()
    {
        var (source, _, _) = Create(l => l.GzipBodies = true);

        HeightAt(await source.SearchAsync(Center, 200), Center).Should().BeApproximately(25, 0.01f);
    }

    [Fact]
    public async Task SearchAsync_ShouldSendTheKey_OnlyWhenOneIsConfigured()
    {
        var (withKey, _, keyedHandler) = Create(esriOptions: new EsriOptions { ApiKey = "test-key" });
        await withKey.SearchAsync(Center, 200);
        keyedHandler.LastRequest!.Headers.GetValues("X-Esri-Authorization").Should().Equal("Bearer test-key");

        var (withoutKey, _, keylessHandler) = Create();
        await withoutKey.SearchAsync(Center, 200);
        keylessHandler.LastRequest!.Headers.Contains("X-Esri-Authorization").Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_ShouldCacheTheResult_AndTheNodePages()
    {
        var (source, layer, _) = Create();

        await source.SearchAsync(Center, 200);
        var afterFirst = layer.Requests.Count;
        await source.SearchAsync(Center, 200);
        layer.Requests.Should().HaveCount(afterFirst, "the same search is answered from the cache");

        await source.SearchAsync(new GeoPoint(Center.Latitude + 0.0001, Center.Longitude), 200);
        layer.Requests.Skip(afterFirst).Should().NotContain(r => r.Contains("/nodepages/") || r.EndsWith("?f=json"),
            "a nearby search reuses the layer description and node pages, fetching only the leaf data");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task SearchAsync_ShouldThrowTheTypedUnavailableException_WhenTheLayerFails(HttpStatusCode status)
    {
        var (source, _, _) = Create(l => l.FailWith = status);

        var act = async () => await source.SearchAsync(Center, 200);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowTheTypedUnavailableException_WhenTheAttributesDisagree()
    {
        var (source, _, _) = Create(l => l.Sources = ["Vantor"]);

        var act = async () => await source.SearchAsync(Center, 200);

        await act.Should().ThrowAsync<BuildingProviderUnavailableException>();
    }

    private const string ExpiredTokenError =
        """{"error":{"code":498,"message":"Invalid Token.","details":["Token would have expired, regenerate token and send the request again."]}}""";

    [Fact]
    public async Task SearchAsync_ShouldDropARejectedKey_AndReadThePublicLayerWithoutIt()
    {
        // Live, 2026-09-26: an expired key made ArcGIS answer HTTP 200 with this error document for
        // leaf resources, which then decoded as "not a Draco file" or as an attribute count in the billions.
        var (source, layer, _) = Create(
            l => { l.LeafErrorBody = ExpiredTokenError; l.LeafErrorOnlyWithKey = true; },
            new EsriOptions { ApiKey = "expired-key" });

        var heights = await source.SearchAsync(Center, 200);

        HeightAt(heights, Center).Should().BeApproximately(25, 0.01f);
        var afterFirst = layer.Requests.Count;
        await source.SearchAsync(new GeoPoint(Center.Latitude + 0.0001, Center.Longitude), 200);
        layer.Requests.Should().HaveCount(afterFirst + 3,
            "once rejected, the key is not sent again, so each leaf resource is fetched once rather than twice");
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowTheTypedUnavailableException_WhenTheLayerAnswersWithAnErrorDocument()
    {
        var (source, _, _) = Create(l => l.LeafErrorBody = ExpiredTokenError);

        var act = async () => await source.SearchAsync(Center, 200);

        (await act.Should().ThrowAsync<BuildingProviderUnavailableException>())
            .WithInnerException<InvalidDataException>().WithMessage("*498*Invalid Token*");
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnNothingWithoutAnyRequest_WhenDisabled()
    {
        var (source, layer, _) = Create(enabled: false);

        (await source.SearchAsync(Center, 200)).HasMeasurements.Should().BeFalse();
        layer.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(3.0, "OSM", false)]
    [InlineData(3.0, "Vantor", true)]
    [InlineData(3.0, "vantor 2024", true)]
    [InlineData(3.5, "OSM", true)]
    [InlineData(0.0, "Vantor", false)]
    [InlineData(double.NaN, "Vantor", false)]
    public void IsMeasured_ShouldTrustVantor_AndRejectOsmPlaceholders(double height, string source, bool expected)
    {
        EsriBuildingHeightSource.IsMeasured(height, source).Should().Be(expected);
    }

    internal static byte[] Float32Attribute(float[] values)
    {
        var bytes = new byte[4 + (4 * values.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)values.Length);
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4 + (4 * i)), values[i]);
        return bytes;
    }

    internal static byte[] StringAttribute(string[] values)
    {
        var encoded = values.Select(v => Encoding.UTF8.GetBytes(v + "\0")).ToList();
        var bytes = new List<byte>();
        void UInt32(int value)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)value);
            bytes.AddRange(buffer);
        }

        UInt32(values.Length);
        UInt32(encoded.Sum(e => e.Length));
        foreach (var e in encoded) UInt32(e.Length);
        foreach (var e in encoded) bytes.AddRange(e);
        return [.. bytes];
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }
}
