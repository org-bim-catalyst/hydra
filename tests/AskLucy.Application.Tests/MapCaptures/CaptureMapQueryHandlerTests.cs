using AskLucy.Application.Locations;
using AskLucy.Application.MapCaptures;
using AskLucy.Application.MapCaptures.Queries.CaptureMap;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.MapCaptures;

/// <summary>
/// specs/083-viewer-map-capture T009 — location precedence, typed failures, defaults, and the
/// stateless constructor (no storage port, FR-014/SC-004).
/// </summary>
public sealed class CaptureMapQueryHandlerTests
{
    private readonly IGeocodingProvider _geocoding = Substitute.For<IGeocodingProvider>();
    private readonly IMapImageRenderer _renderer = Substitute.For<IMapImageRenderer>();
    private readonly MapCaptureOptions _options = new();
    private readonly CaptureMapQueryHandler _handler;

    public CaptureMapQueryHandlerTests()
    {
        _handler = new CaptureMapQueryHandler(
            _geocoding, _renderer, Microsoft.Extensions.Options.Options.Create(_options),
            NullLogger<CaptureMapQueryHandler>.Instance);
    }

    private static CaptureMapQuery Query(
        string? location = null, double? latitude = null, double? longitude = null,
        int? viewSizeMetres = null, string? mode = null, string? overlays = null,
        string caller = "pipeline") =>
        new(location, latitude, longitude, viewSizeMetres, mode, overlays, caller);

    private void RendererSucceeds() =>
        _renderer.RenderAsync(Arg.Any<MapImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(MapImageResult.Success([1, 2, 3], 1252, 1252));

    [Fact]
    public async Task Handle_ShouldUseCoordinatesAndSkipGeocoding_WhenBothAreSupplied()
    {
        RendererSucceeds();

        var result = await _handler.Handle(
            Query(location: "ignored place", latitude: 25.2, longitude: 55.3),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _geocoding.DidNotReceiveWithAnyArgs().SearchAsync(default!, Arg.Any<CancellationToken>());
        await _renderer.Received(1).RenderAsync(
            Arg.Is<MapImageRequest>(r =>
                r != null &&
                r.Latitude == 25.2 && r.Longitude == 55.3 && r.LocationName == "25.2,55.3"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldTakeTheHighestImportanceCandidate_ForAPlaceName()
    {
        RendererSucceeds();
        _geocoding.SearchAsync("Al Safa Park 2", Arg.Any<CancellationToken>()).Returns(
        [
            new GeocodingCandidate("Side street", 25.1, 55.1, 0.0),
            new GeocodingCandidate("Al Safa Park 2", 25.1801, 55.2451, 0.0801),
            new GeocodingCandidate("Al Safa Park", 25.17, 55.24, 0.06),
        ]);

        var result = await _handler.Handle(
            Query(location: "Al Safa Park 2"), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _renderer.Received(1).RenderAsync(
            Arg.Is<MapImageRequest>(r =>
                r != null &&
                r.Latitude == 25.1801 && r.Longitude == 55.2451 && r.LocationName == "Al Safa Park 2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnLocationNotFound_WhenGeocodingReturnsNoCandidates()
    {
        _geocoding.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<GeocodingCandidate>>([]));

        var result = await _handler.Handle(
            Query(location: "nowhere in particular"), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(MapCaptureFailureReason.LocationNotFound);
        result.FailureDetail.Should().Contain("nowhere in particular");
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnGeocodingUnavailable_WhenTheGeocoderThrows()
    {
        _geocoding.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<GeocodingCandidate>>(_ => throw new GeocodingProviderUnavailableException("down"));

        var result = await _handler.Handle(
            Query(location: "Dubai"), TestContext.Current.CancellationToken);

        result.FailureReason.Should().Be(MapCaptureFailureReason.GeocodingUnavailable);
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldDefaultToSatelliteEmptyOverlaysAndTheConfiguredViewSize()
    {
        RendererSucceeds();

        await _handler.Handle(
            Query(latitude: 25.2, longitude: 55.3, caller: "pipeline"), TestContext.Current.CancellationToken);

        await _renderer.Received(1).RenderAsync(
            Arg.Is<MapImageRequest>(r =>
                r != null &&
                r.Mode == MapCaptureMode.Satellite
                && r.Overlays.Count == 0
                && r.ViewSizeMetres == _options.DefaultViewSizeMetres
                && r.CallerName == "pipeline"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldParseHyphenatedModeAndOverlays()
    {
        RendererSucceeds();

        await _handler.Handle(
            Query(latitude: 25.2, longitude: 55.3, mode: "buildings-only", overlays: "building-footprints"),
            TestContext.Current.CancellationToken);

        await _renderer.Received(1).RenderAsync(
            Arg.Is<MapImageRequest>(r =>
                r != null &&
                r.Mode == MapCaptureMode.BuildingsOnly
                && r.Overlays.SequenceEqual(new[] { MapCaptureOverlay.BuildingFootprints })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRejectSiteBoundary_BeforeCallingTheRenderer()
    {
        var result = await _handler.Handle(
            Query(latitude: 25.2, longitude: 55.3, overlays: "building-footprints,site-boundary"),
            TestContext.Current.CancellationToken);

        result.FailureReason.Should().Be(MapCaptureFailureReason.OverlayUnavailable);
        result.FailureDetail.Should().Contain("site-boundary");
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnTheRendererFailureUnchanged()
    {
        var failure = MapImageResult.Failure(MapCaptureFailureReason.ProviderUnavailable, "upstream down");
        _renderer.RenderAsync(Arg.Any<MapImageRequest>(), Arg.Any<CancellationToken>()).Returns(failure);

        var result = await _handler.Handle(
            Query(latitude: 25.2, longitude: 55.3), TestContext.Current.CancellationToken);

        result.Should().BeSameAs(failure);
    }

    [Fact]
    public void Constructor_ShouldTakeNoStoragePort()
    {
        var parameterTypes = typeof(CaptureMapQueryHandler).GetConstructors()
            .Single().GetParameters().Select(p => p.ParameterType).ToArray();

        parameterTypes.Should().Equal(
            typeof(IGeocodingProvider),
            typeof(IMapImageRenderer),
            typeof(IOptions<MapCaptureOptions>),
            typeof(ILogger<CaptureMapQueryHandler>));
    }
}
