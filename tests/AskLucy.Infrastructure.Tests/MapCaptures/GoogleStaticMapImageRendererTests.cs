using System.Net;
using System.Net.Http.Headers;
using System.Web;
using AskLucy.Application.Buildings;
using AskLucy.Application.MapCaptures;
using AskLucy.Domain.SiteBoundaries;
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

/// <summary>specs/083-viewer-map-capture T011 — mode/style mapping and provider failure typing.</summary>
public sealed class GoogleStaticMapImageRendererTests
{
    private static readonly byte[] ProviderPng = CreatePng();

    private static byte[] CreatePng()
    {
        using var image = new MagickImage(MagickColors.Red, 1280, 1280);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static IBuildingFootprintProvider EmptyFootprints()
    {
        var provider = Substitute.For<IBuildingFootprintProvider>();
        provider.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new BuildingFootprintResult([], false, 0, 500));
        return provider;
    }

    private static GoogleStaticMapImageRenderer CreateRenderer(
        out StubHttpMessageHandler handler,
        string apiKey = "test-key",
        HttpStatusCode status = HttpStatusCode.OK,
        string contentType = "image/png",
        IBuildingFootprintProvider? footprints = null)
    {
        handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(ProviderPng) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return response;
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleStaticMaps")
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://maps.googleapis.com/maps/api/") });

        return new GoogleStaticMapImageRenderer(
            factory,
            Options.Create(new GoogleMapsGeocodingOptions { GoogleMapsApiKey = apiKey }),
            footprints ?? EmptyFootprints(),
            new MapImageComposer(Options.Create(new MapCaptureOptions()), NullLogger<MapImageComposer>.Instance),
            NullLogger<GoogleStaticMapImageRenderer>.Instance);
    }

    private static MapImageRequest Request(
        MapCaptureMode mode = MapCaptureMode.Satellite,
        IReadOnlyList<MapCaptureOverlay>? overlays = null) =>
        new(25.2, 55.3, "Test", mode, overlays ?? [], 1000, "pipeline");

    private static string QueryOf(StubHttpMessageHandler handler) =>
        HttpUtility.ParseQueryString(handler.LastRequest!.RequestUri!.Query)["maptype"]!;

    [Theory]
    [InlineData(MapCaptureMode.Roadmap, "roadmap")]
    [InlineData(MapCaptureMode.Satellite, "satellite")]
    [InlineData(MapCaptureMode.Hybrid, "hybrid")]
    [InlineData(MapCaptureMode.BuildingsOnly, "roadmap")]
    public async Task RenderAsync_ShouldMapModeToMaptype(MapCaptureMode mode, string expected)
    {
        var renderer = CreateRenderer(out var handler);

        var result = await renderer.RenderAsync(Request(mode), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        QueryOf(handler).Should().Be(expected);
    }

    [Fact]
    public async Task RenderAsync_ShouldRequestDoubleResolutionPng()
    {
        var renderer = CreateRenderer(out var handler);

        await renderer.RenderAsync(Request(), TestContext.Current.CancellationToken);

        var query = HttpUtility.ParseQueryString(handler.LastRequest!.RequestUri!.Query);
        query["size"].Should().Be("640x640");
        query["scale"].Should().Be("2");
        query["format"].Should().Be("png");
        query["center"].Should().Be("25.2,55.3");
    }

    [Fact]
    public async Task RenderAsync_ShouldAddBuildingsOnlyStyles()
    {
        var renderer = CreateRenderer(out var handler);

        await renderer.RenderAsync(Request(MapCaptureMode.BuildingsOnly), TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.Query.Should().Contain("landscape.man_made");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "image/png")]
    [InlineData(HttpStatusCode.OK, "text/html")]
    public async Task RenderAsync_ShouldReturnProviderUnavailable_ForBadResponses(
        HttpStatusCode status, string contentType)
    {
        var renderer = CreateRenderer(out _, status: status, contentType: contentType);

        var result = await renderer.RenderAsync(Request(), TestContext.Current.CancellationToken);

        result.FailureReason.Should().Be(MapCaptureFailureReason.ProviderUnavailable);
    }

    [Fact]
    public async Task RenderAsync_ShouldReturnModeUnavailable_WhenNoApiKeyIsConfigured()
    {
        var renderer = CreateRenderer(out var handler, apiKey: "");

        var result = await renderer.RenderAsync(Request(), TestContext.Current.CancellationToken);

        result.FailureReason.Should().Be(MapCaptureFailureReason.ModeUnavailable);
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task RenderAsync_ShouldFetchFootprintsOverHalfTheViewSize()
    {
        var footprints = Substitute.For<IBuildingFootprintProvider>();
        footprints.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new BuildingFootprintResult([], false, 0, 500));
        var renderer = CreateRenderer(out _, footprints: footprints);

        await renderer.RenderAsync(
            Request(overlays: [MapCaptureOverlay.BuildingFootprints]), TestContext.Current.CancellationToken);

        await footprints.Received(1).SearchAsync(
            Arg.Is<GeoPoint>(p => p != null && p.Latitude == 25.2 && p.Longitude == 55.3),
            500,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenderAsync_ShouldReturnOverlayUnavailable_WhenFootprintsThrow()
    {
        var footprints = Substitute.For<IBuildingFootprintProvider>();
        footprints.SearchAsync(Arg.Any<GeoPoint>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<BuildingFootprintResult>(_ => throw new HttpRequestException("overpass down"));
        var renderer = CreateRenderer(out _, footprints: footprints);

        var result = await renderer.RenderAsync(
            Request(overlays: [MapCaptureOverlay.BuildingFootprints]), TestContext.Current.CancellationToken);

        result.FailureReason.Should().Be(MapCaptureFailureReason.OverlayUnavailable);
    }
}
