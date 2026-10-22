using System.Net;
using System.Net.Http.Headers;
using System.Web;
using AskLucy.Application.Buildings;
using AskLucy.Application.MapCaptures;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Geocoding;
using AskLucy.Infrastructure.MapCaptures;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.MapCaptures;

/// <summary>
/// specs/083-viewer-map-capture T013 — the router picks the provider the request asked for, and
/// refuses what that provider cannot serve rather than quietly answering from the other one.
/// </summary>
public sealed class MapImageRendererRouterTests
{
    private static readonly byte[] ProviderPng = CreatePng();

    private static byte[] CreatePng()
    {
        using var image = new MagickImage(MagickColors.Red, 1280, 1280);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static StubHttpMessageHandler NewHandler() =>
        new(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ProviderPng) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        });

    private static (
        MapImageRendererRouter Router,
        StubHttpMessageHandler Google,
        StubHttpMessageHandler Esri,
        IBuildingFootprintProvider Footprints) Create(string googleKey = "test-key", string? esriKey = null)
    {
        var googleHandler = NewHandler();
        var esriHandler = NewHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleStaticMaps")
            .Returns(new HttpClient(googleHandler) { BaseAddress = new Uri("https://maps.googleapis.com/maps/api/") });
        factory.CreateClient("EsriWorldImagery").Returns(new HttpClient(esriHandler));

        var footprints = Substitute.For<IBuildingFootprintProvider>();
        footprints.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new BuildingFootprintResult([], false, 0, 500));

        var composer = new MapImageComposer(
            Options.Create(new MapCaptureOptions()), NullLogger<MapImageComposer>.Instance);
        var googleOptions = Options.Create(new GoogleMapsGeocodingOptions { GoogleMapsApiKey = googleKey });
        var google = new GoogleStaticMapImageRenderer(
            factory, googleOptions, footprints, composer, NullLogger<GoogleStaticMapImageRenderer>.Instance);
        var esri = new EsriSatelliteImageRenderer(
            factory, Options.Create(new EsriOptions { ApiKey = esriKey }), footprints, composer,
            NullLogger<EsriSatelliteImageRenderer>.Instance);

        return (new MapImageRendererRouter(google, esri, googleOptions), googleHandler, esriHandler, footprints);
    }

    private static MapImageRequest Request(
        MapCaptureMode mode = MapCaptureMode.Satellite,
        IReadOnlyList<MapCaptureOverlay>? overlays = null,
        MapCaptureSource source = MapCaptureSource.Esri,
        int? zoom = null) =>
        new(25.2, 55.3, "Test", mode, overlays ?? [], 1000, "pipeline", source, 32, zoom, false);

    [Fact]
    public async Task RenderAsync_ShouldUseEsri_WhenTheRequestAsksForEsri()
    {
        var (router, google, esri, _) = Create();

        var result = await router.RenderAsync(Request(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        esri.LastRequest.Should().NotBeNull();
        google.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task RenderAsync_ShouldUseGoogle_WhenTheRequestAsksForGoogle()
    {
        var (router, google, esri, _) = Create();

        var result = await router.RenderAsync(
            Request(source: MapCaptureSource.Google), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        google.LastRequest.Should().NotBeNull();
        esri.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task RenderAsync_ShouldRefuseGoogle_WhenNoKeyIsConfigured()
    {
        var (router, google, esri, _) = Create(googleKey: "");

        var result = await router.RenderAsync(
            Request(source: MapCaptureSource.Google), TestContext.Current.CancellationToken);

        result.FailureReason.Should().Be(MapCaptureFailureReason.ModeUnavailable);
        google.LastRequest.Should().BeNull();
        esri.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task RenderAsync_ShouldRefuseANonSatelliteMode_OnEsri()
    {
        var (router, google, esri, _) = Create();

        var result = await router.RenderAsync(
            Request(mode: MapCaptureMode.Hybrid), TestContext.Current.CancellationToken);

        result.FailureReason.Should().Be(MapCaptureFailureReason.ModeUnavailable);
        google.LastRequest.Should().BeNull();
        esri.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task RenderAsync_ShouldRequestTheEsriExportFromTheHostThatServesIt()
    {
        var (router, _, esri, _) = Create();

        await router.RenderAsync(Request(), TestContext.Current.CancellationToken);

        var request = esri.LastRequest!.RequestUri!;
        request.Host.Should().Be("services.arcgisonline.com",
            "the ArcGIS Location Platform host answers 404 for this export path");
        var query = HttpUtility.ParseQueryString(request.Query);
        query["size"].Should().Be("1280,1280");
        query["format"].Should().Be("png");
        query["bboxSR"].Should().Be("4326");
        query["token"].Should().BeNull("no key is configured in this test");
    }

    [Fact]
    public async Task RenderAsync_ShouldPassTheConfiguredEsriKeyAsAToken()
    {
        var (router, _, esri, _) = Create(esriKey: "test-esri-key");

        await router.RenderAsync(Request(), TestContext.Current.CancellationToken);

        HttpUtility.ParseQueryString(esri.LastRequest!.RequestUri!.Query)["token"].Should().Be("test-esri-key");
    }

    [Fact]
    public async Task RenderAsync_ShouldRefuseAZoomEsriCannotAssemble()
    {
        var (router, google, esri, _) = Create();

        var result = await router.RenderAsync(
            Request(zoom: MapCaptureZoom.MaximumEsri + 1), TestContext.Current.CancellationToken);

        result.FailureReason.Should().Be(MapCaptureFailureReason.ZoomUnavailable);
        esri.LastRequest.Should().BeNull();
        google.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task RenderAsync_ShouldPaintFootprintsOnTheEsriPath()
    {
        var (router, _, esri, footprints) = Create();

        var result = await router.RenderAsync(
            Request(overlays: [MapCaptureOverlay.BuildingFootprints]),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        esri.LastRequest.Should().NotBeNull();
        await footprints.Received(1).SearchAsync(
            Arg.Any<GeoPoint>(), 500, Arg.Any<CancellationToken>());
    }
}
