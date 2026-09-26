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

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests) Requests.Add(url);
            if (FailWith is { } status) return new HttpResponseMessage(status);

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

    /// <summary>
    /// Feature 0, 1 and 2 at the site; feature 3 two kilometres north, outside a 200 m search.
    /// Hand-written: NSubstitute cannot proxy an internal interface.
    /// </summary>
    private sealed class FakeDecoder : II3sGeometryDecoder
    {
        public IReadOnlyList<(double Longitude, double Latitude)?> DecodeFeatureCentres(
            byte[] geometry, double nodeCenterLongitude, double nodeCenterLatitude, int featureCount) =>
        [
            (Center.Longitude, Center.Latitude),
            (Center.Longitude + 0.0002, Center.Latitude),
            (Center.Longitude, Center.Latitude + 0.0002),
            (Center.Longitude, Center.Latitude + 0.018),
        ];
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
            new FakeDecoder(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new EsriBuildingsOptions { SceneLayerUrl = LayerUrl, Enabled = enabled }),
            Options.Create(esriOptions ?? new EsriOptions()),
            Options.Create(new BuildingRetrievalOptions()),
            NullLogger<EsriBuildingHeightSource>.Instance);
        return (source, layer, handler);
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnMeasuredHeightsInRange_AndDropPlaceholders()
    {
        var (source, _, _) = Create();

        var heights = await source.SearchAsync(Center, 200);

        heights.Select(h => h.HeightMetres).Should().BeEquivalentTo(new[] { 25.0, 3.0 },
            "Vantor's 25 m and 3 m are measurements; OSM's 3 m is the layer's no-height placeholder; OSM's 12 m is 2 km away");
        heights.Should().Contain(h => h.Location == Center);
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

        (await source.SearchAsync(Center, 200)).Should().HaveCount(2);
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

    [Fact]
    public async Task SearchAsync_ShouldReturnNothingWithoutAnyRequest_WhenDisabled()
    {
        var (source, layer, _) = Create(enabled: false);

        (await source.SearchAsync(Center, 200)).Should().BeEmpty();
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
