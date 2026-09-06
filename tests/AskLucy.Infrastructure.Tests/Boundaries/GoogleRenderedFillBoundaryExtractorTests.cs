using System.Net;
using System.Net.Http.Headers;
using System.Web;
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
/// §9.8 — the deterministic rendered-fill path. The tracing/simplification algorithm itself is
/// already exhaustively covered by <see cref="MaskContourVectorizerTests"/> (this class delegates
/// to it once it has a colour mask); what's specific to this class is the request it builds (must
/// force the right feature's fill to the exact colour it then thresholds for) and its degrade
/// behaviour when Google doesn't cooperate.
/// </summary>
public sealed class GoogleRenderedFillBoundaryExtractorTests
{
    private static readonly GeoPoint AlSafaCenter = new(25.1558327, 55.2217644);

    private static GoogleRenderedFillBoundaryExtractor CreateExtractor(
        out List<Uri> requestedUris, string apiKey = "test-key", HttpStatusCode status = HttpStatusCode.OK, byte[]? responseBytes = null)
    {
        var captured = new List<Uri>();
        requestedUris = captured;

        var handler = new StubHttpMessageHandler(request =>
        {
            captured.Add(request.RequestUri!);
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(responseBytes ?? [1, 2, 3]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GoogleStaticMaps")
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://maps.googleapis.com/maps/api/") });

        return new GoogleRenderedFillBoundaryExtractor(
            factory,
            Options.Create(new GoogleMapsGeocodingOptions { GoogleMapsApiKey = apiKey }),
            NullLogger<GoogleRenderedFillBoundaryExtractor>.Instance);
    }

    [Fact]
    public async Task TryExtractAsync_ShouldForceTheRequestedFeatureToAKnownDistinctColour()
    {
        var extractor = CreateExtractor(out var requested);

        await extractor.TryExtractAsync(AlSafaCenter, 150, "poi.park", TestContext.Current.CancellationToken);

        var query = HttpUtility.ParseQueryString(requested.Single().Query);
        var styles = query.GetValues("style");
        styles.Should().NotBeNull();
        // Pure green: nothing else Static Maps' roadmap style renders is a saturated green, so
        // thresholding for it later has no ambiguity to resolve.
        styles.Should().Contain("feature:poi.park|element:geometry.fill|color:0x00FF00");
        query["maptype"].Should().Be("roadmap");
    }

    [Fact]
    public async Task TryExtractAsync_ShouldTurnOffAllLabels()
    {
        // Confirmed live: a label or marker icon drawn on top of the fill (the site's own name
        // label + pin, a POI marker sitting inside it) punches a non-green hole through it,
        // indistinguishable from a real gap in the boundary to a pixel threshold.
        var extractor = CreateExtractor(out var requested);

        await extractor.TryExtractAsync(AlSafaCenter, 150, "poi.park", TestContext.Current.CancellationToken);

        var query = HttpUtility.ParseQueryString(requested.Single().Query);
        var styles = query.GetValues("style");
        styles.Should().Contain("feature:all|element:labels|visibility:off");
    }

    [Fact]
    public async Task TryExtractAsync_ShouldReturnNull_WhenNoApiKeyIsConfigured()
    {
        var extractor = CreateExtractor(out var requested, apiKey: "");

        var ring = await extractor.TryExtractAsync(AlSafaCenter, 150, "poi.park", TestContext.Current.CancellationToken);

        ring.Should().BeNull("no key means this enhancement is simply off");
        requested.Should().BeEmpty();
    }

    [Fact]
    public async Task TryExtractAsync_ShouldReturnNull_RatherThanThrow_WhenGoogleRejectsTheRequest()
    {
        var extractor = CreateExtractor(out _, status: HttpStatusCode.Forbidden);

        var ring = await extractor.TryExtractAsync(AlSafaCenter, 150, "poi.park", TestContext.Current.CancellationToken);

        ring.Should().BeNull();
    }

    [Fact]
    public async Task TryExtractAsync_ShouldReturnNull_RatherThanThrow_WhenTheResponseIsNotADecodableImage()
    {
        // This class decodes the image itself (unlike GoogleSatelliteImageProvider, which only
        // ever hands raw bytes onward) - a malformed body must degrade to null, not throw.
        var extractor = CreateExtractor(out _, responseBytes: [1, 2, 3, 4, 5]);

        var ring = await extractor.TryExtractAsync(AlSafaCenter, 150, "poi.park", TestContext.Current.CancellationToken);

        ring.Should().BeNull();
    }
}
