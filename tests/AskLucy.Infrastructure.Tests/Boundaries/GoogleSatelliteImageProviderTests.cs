using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Web;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Geocoding;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>
/// The vision cross-check's imagery now comes from Google Static Maps, the same provider the
/// viewer renders on, framed on the site rather than on the search radius.
/// </summary>
/// <remarks>
/// Both properties are worth pinning. The provider decides how much ground the analyzer gets to
/// look at, and it reports the bounds that the analyzer uses to turn normalised pixel coordinates
/// back into latitude/longitude — so a wrong frame or wrong bounds silently corrupts every
/// correction downstream rather than failing visibly.
/// </remarks>
public sealed class GoogleSatelliteImageProviderTests
{
    private static readonly GeoPoint AlSafaCenter = new(25.1558327, 55.2217644);

    private static GoogleSatelliteImageProvider CreateProvider(
        out List<Uri> requestedUris, string apiKey = "test-key", HttpStatusCode status = HttpStatusCode.OK)
    {
        var captured = new List<Uri>();
        requestedUris = captured;

        var handler = new StubHttpMessageHandler(request =>
        {
            captured.Add(request.RequestUri!);
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleStaticMaps")
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://maps.googleapis.com/maps/api/") });

        return new GoogleSatelliteImageProvider(
            factory,
            Options.Create(new GoogleMapsGeocodingOptions { GoogleMapsApiKey = apiKey }),
            NullLogger<GoogleSatelliteImageProvider>.Instance);
    }

    private static int ZoomOf(Uri uri) =>
        int.Parse(HttpUtility.ParseQueryString(uri.Query)["zoom"]!, CultureInfo.InvariantCulture);

    [Fact]
    public async Task FetchAsync_ShouldRequestRoadmapImageryFromGoogle_AtDoubleResolution()
    {
        var provider = CreateProvider(out var requested);

        var image = await provider.FetchAsync(AlSafaCenter, 120, TestContext.Current.CancellationToken);

        image.Should().NotBeNull();
        var query = requested.Single().ToString();
        // Roadmap, not satellite: Gemini traces the polygon Google's own map rendering already
        // drew for the site, rather than inferring a fence line from photography — see
        // GoogleSatelliteImageProvider's remarks.
        query.Should().Contain("maptype=roadmap");
        // scale=2 returns 1280 px for the same ground as 640 — the difference between a fence
        // being a pixel wide and being several.
        query.Should().Contain("scale=2");
        query.Should().Contain("size=640x640");
        // JPEG, not PNG: these bytes ride inside the vision request, so their size is part of the
        // 30s vision budget. The same frame is 1.51 MB base64 as PNG and 449 KB as JPEG, and
        // requests carrying the PNG were still in flight when the budget expired.
        query.Should().Contain("format=jpg");
    }

    /// <summary>
    /// A tighter site must produce a closer frame. This is the whole point of the change: the old
    /// provider took the 500 m search radius regardless, so a 90 x 166 m park sat in a 1 km frame
    /// with no fence resolvable at any resolution.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(500)]
    public async Task FetchAsync_ShouldZoomIn_AsTheRequestedRadiusShrinks(int radiusMeters)
    {
        var provider = CreateProvider(out var requested);

        await provider.FetchAsync(AlSafaCenter, radiusMeters, TestContext.Current.CancellationToken);
        await provider.FetchAsync(AlSafaCenter, radiusMeters * 2, TestContext.Current.CancellationToken);

        ZoomOf(requested[0]).Should().BeGreaterThan(
            ZoomOf(requested[1]), "halving the ground covered is one more zoom level");
    }

    /// <summary>
    /// Integer zoom always covers at least what was asked for, never less: a clipped site loses
    /// the very corners the analyzer is meant to be reading.
    /// </summary>
    [Theory]
    [InlineData(60)]
    [InlineData(112)]
    [InlineData(300)]
    public async Task FetchAsync_ShouldReportBounds_ThatContainTheRequestedRadius(int radiusMeters)
    {
        var provider = CreateProvider(out _);

        var image = await provider.FetchAsync(AlSafaCenter, radiusMeters, TestContext.Current.CancellationToken);

        var northEdge = GeometryMath.DistanceMeters(AlSafaCenter, new GeoPoint(image!.North, AlSafaCenter.Longitude));
        var eastEdge = GeometryMath.DistanceMeters(AlSafaCenter, new GeoPoint(AlSafaCenter.Latitude, image.East));

        northEdge.Should().BeGreaterThanOrEqualTo(radiusMeters);
        eastEdge.Should().BeGreaterThanOrEqualTo(radiusMeters);
    }

    /// <summary>
    /// The reported bounds are what the analyzer interpolates against to turn a normalised [0,1]
    /// trace back into coordinates, so they must be centred on the point that was requested.
    /// </summary>
    [Fact]
    public async Task FetchAsync_ShouldReportBounds_CentredOnTheRequestedPoint()
    {
        var provider = CreateProvider(out _);

        var image = await provider.FetchAsync(AlSafaCenter, 150, TestContext.Current.CancellationToken);

        var midLatitude = (image!.North + image.South) / 2;
        var midLongitude = (image.West + image.East) / 2;

        GeometryMath.DistanceMeters(new GeoPoint(midLatitude, midLongitude), AlSafaCenter)
            .Should().BeLessThan(1, "Mercator's latitude curvature costs well under a metre at this scale");
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnNull_WhenNoApiKeyIsConfigured()
    {
        var provider = CreateProvider(out var requested, apiKey: "");

        var image = await provider.FetchAsync(AlSafaCenter, 150, TestContext.Current.CancellationToken);

        image.Should().BeNull("no key means this enhancement is simply off");
        requested.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnNull_RatherThanThrow_WhenGoogleRejectsTheRequest()
    {
        var provider = CreateProvider(out _, status: HttpStatusCode.Forbidden);

        var image = await provider.FetchAsync(AlSafaCenter, 150, TestContext.Current.CancellationToken);

        image.Should().BeNull("ISatelliteImageProvider degrades to 'vision unavailable', it never throws");
    }
}
