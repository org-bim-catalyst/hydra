using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
/// Ground-level cross-check imagery, aimed from points near a site's perimeter back toward it.
/// </summary>
/// <remarks>
/// The behaviour worth pinning here is the metadata-first sequencing: the Street View Static API
/// answers a location with no coverage with 200 OK and a generic gray placeholder image, not a
/// 404 — so <c>streetview/metadata</c> is checked first, and the image request uses that
/// response's own <c>pano_id</c> rather than trusting a second location+radius lookup to snap to
/// the same panorama.
/// </remarks>
public sealed class GoogleStreetViewImageProviderTests
{
    private static readonly GeoPoint LookAt = new(25.1558, 55.2218);
    private static readonly GeoPoint ViewpointA = new(25.1550, 55.2218); // due south of LookAt
    private static readonly GeoPoint ViewpointB = new(25.1558, 55.2210); // due west of LookAt

    private static string LocationQueryOf(Uri uri) =>
        HttpUtility.ParseQueryString(uri.Query)["location"] ?? "";

    /// <summary>The exact "lat,lng" string the provider itself builds — matched exactly, never by
    /// prefix, so two viewpoints that happen to share leading digits can't be confused.</summary>
    private static string ExpectedLocationQuery(GeoPoint point) =>
        point.Latitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ","
        + point.Longitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static string? PanoQueryOf(Uri uri) =>
        HttpUtility.ParseQueryString(uri.Query)["pano"];

    private static double HeadingQueryOf(Uri uri) =>
        double.Parse(HttpUtility.ParseQueryString(uri.Query)["heading"]!, System.Globalization.CultureInfo.InvariantCulture);

    private static HttpResponseMessage MetadataOk(string panoId, GeoPoint snappedTo)
    {
        // Built by concatenation rather than a raw interpolated string: the nested
        // {"location":{"lat":...}} braces sit directly against the interpolation's own closing
        // braces, which a $$"""...""" literal can't parse unambiguously at this nesting depth.
        var json = "{\"status\":\"OK\",\"pano_id\":\"" + panoId + "\","
            + "\"location\":{\"lat\":" + snappedTo.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"lng\":" + snappedTo.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage MetadataZeroResults() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"ZERO_RESULTS"}""", Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage ImageResponse(byte[] bytes) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } },
        };

    private static GoogleStreetViewImageProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out ConcurrentBag<Uri> requestedUris, string apiKey = "test-key")
    {
        var captured = new ConcurrentBag<Uri>();
        requestedUris = captured;

        var handler = new StubHttpMessageHandler(request =>
        {
            captured.Add(request.RequestUri!);
            return responder(request);
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleStaticMaps")
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://maps.googleapis.com/maps/api/") });

        return new GoogleStreetViewImageProvider(
            factory,
            Options.Create(new GoogleMapsGeocodingOptions { GoogleMapsApiKey = apiKey }),
            NullLogger<GoogleStreetViewImageProvider>.Instance);
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnOneImagePerViewpoint_InTheRequestedOrder()
    {
        var locationA = ExpectedLocationQuery(ViewpointA);
        var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("metadata", StringComparison.Ordinal))
            {
                return LocationQueryOf(request.RequestUri!) == locationA
                    ? MetadataOk("pano-a", ViewpointA)
                    : MetadataOk("pano-b", ViewpointB);
            }

            var pano = PanoQueryOf(request.RequestUri!);
            return ImageResponse(pano == "pano-a" ? [1, 1, 1] : [2, 2, 2]);
        }, out _);

        var images = await provider.FetchAsync([ViewpointA, ViewpointB], LookAt, TestContext.Current.CancellationToken);

        images.Should().HaveCount(2);
        // Concurrent fetch, but the result preserves the order viewpoints were requested in -
        // callers (BoundaryResolutionService) hand these straight to the vision prompt, where a
        // consistent, predictable order makes the prompt's own per-photo labelling meaningful.
        images[0].ImageBytes.Should().Equal(1, 1, 1);
        images[1].ImageBytes.Should().Equal(2, 2, 2);
    }

    /// <summary>
    /// The whole reason for calling metadata first: a location with no coverage answers 200 OK
    /// with a generic placeholder image, indistinguishable from a real photo by status code or
    /// content-type alone. A viewpoint metadata reports as uncovered must never reach the vision
    /// analyzer as if it were a real ground-level view.
    /// </summary>
    [Fact]
    public async Task FetchAsync_ShouldOmitAViewpoint_WhenMetadataReportsNoCoverage()
    {
        var locationA = ExpectedLocationQuery(ViewpointA);
        var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("metadata", StringComparison.Ordinal))
            {
                return LocationQueryOf(request.RequestUri!) == locationA
                    ? MetadataZeroResults()
                    : MetadataOk("pano-b", ViewpointB);
            }

            return ImageResponse([2, 2, 2]);
        }, out var requested);

        var images = await provider.FetchAsync([ViewpointA, ViewpointB], LookAt, TestContext.Current.CancellationToken);

        images.Should().ContainSingle();
        images[0].ImageBytes.Should().BeEquivalentTo(new byte[] { 2, 2, 2 });
        // No image request was ever made for the uncovered viewpoint — a metadata failure must
        // stop before the image call, not merely be discarded after it.
        var imagePanosRequested = requested
            .Where(uri => !uri.AbsolutePath.EndsWith("metadata", StringComparison.Ordinal))
            .Select(PanoQueryOf)
            .ToList();
        imagePanosRequested.Should().Equal("pano-b");
    }

    [Fact]
    public async Task FetchAsync_ShouldUseTheMetadataPanoId_NotTheOriginalCoordinates_ForTheImageRequest()
    {
        var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path.EndsWith("metadata", StringComparison.Ordinal)
                ? MetadataOk("the-real-pano-id", ViewpointA)
                : ImageResponse([9]);
        }, out var requested);

        await provider.FetchAsync([ViewpointA], LookAt, TestContext.Current.CancellationToken);

        var imageRequest = requested.Single(uri => !uri.AbsolutePath.EndsWith("metadata", StringComparison.Ordinal));
        PanoQueryOf(imageRequest).Should().Be("the-real-pano-id");
    }

    [Theory]
    [InlineData(25.1550, 55.2218, 0.0)]   // ViewpointA sits due south of LookAt -> heading north
    [InlineData(25.1558, 55.2210, 90.0)]  // ViewpointB sits due west of LookAt -> heading east
    public async Task FetchAsync_ShouldAimTheHeading_FromTheViewpointBackTowardLookAt(
        double viewpointLatitude, double viewpointLongitude, double expectedHeading)
    {
        var viewpoint = new GeoPoint(viewpointLatitude, viewpointLongitude);
        var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path.EndsWith("metadata", StringComparison.Ordinal)
                ? MetadataOk("pano-x", viewpoint)
                : ImageResponse([9]);
        }, out var requested);

        await provider.FetchAsync([viewpoint], LookAt, TestContext.Current.CancellationToken);

        var imageRequest = requested.Single(uri => !uri.AbsolutePath.EndsWith("metadata", StringComparison.Ordinal));
        HeadingQueryOf(imageRequest).Should().BeApproximately(expectedHeading, 1.0);
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnEmpty_WhenNoApiKeyIsConfigured()
    {
        var provider = CreateProvider(_ => throw new InvalidOperationException("must not call out with no key"), out var requested, apiKey: "");

        var images = await provider.FetchAsync([ViewpointA], LookAt, TestContext.Current.CancellationToken);

        images.Should().BeEmpty();
        requested.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnEmpty_WhenNoViewpointsAreGiven()
    {
        var provider = CreateProvider(_ => throw new InvalidOperationException("must not call out with no viewpoints"), out var requested);

        var images = await provider.FetchAsync([], LookAt, TestContext.Current.CancellationToken);

        images.Should().BeEmpty();
        requested.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnEmpty_RatherThanThrow_WhenGoogleRejectsTheMetadataRequest()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), out _);

        var images = await provider.FetchAsync([ViewpointA], LookAt, TestContext.Current.CancellationToken);

        images.Should().BeEmpty("IStreetViewImageProvider degrades to 'no ground-level imagery this run', it never throws");
    }
}
