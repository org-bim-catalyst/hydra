using System.Net;
using AskLucy.Application.Locations;
using AskLucy.Application.MapCaptures;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace AskLucy.Web.Tests.MapCaptures;

/// <summary>
/// specs/083-viewer-map-capture T014 — auth matrix, wire parsing, the failure taxonomy, and the
/// success headers from contracts/map-capture-api.md.
/// </summary>
[Collection(DatabaseSerialGroup.Name)]
public sealed class MapCapturesEndpointTests(MapCapturesWebApplicationFactory factory)
    : IClassFixture<MapCapturesWebApplicationFactory>
{
    private const string Url = "/api/v1/map-captures";

    private HttpClient ClientWithKey() =>
        factory.CreateClient();

    private void DefaultStubs()
    {
        factory.Renderer.ClearSubstitute(ClearOptions.All);
        factory.Geocoding.ClearSubstitute(ClearOptions.All);
        factory.Renderer.RenderAsync(Arg.Any<MapImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(MapImageResult.Success([1, 2, 3], 1252, 1252));
        factory.Geocoding.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<GeocodingCandidate>>(_ =>
                [new GeocodingCandidate("Al Safa Park 2", 25.1801, 55.2451, 0.08)]);
    }

    private static HttpRequestMessage Get(string query, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url + query);
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        return request;
    }

    [Fact]
    public async Task ShouldReturn401_WhenTheKeyIsMissing()
    {
        DefaultStubs();
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?location=Dubai", key: null), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Service authentication required");
    }

    [Fact]
    public async Task ShouldReturn401_WhenTheKeyIsUnknown()
    {
        DefaultStubs();
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?location=Dubai", key: "wrong-key"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ShouldReturnThePngAndHeaders_ForAValidRequest()
    {
        DefaultStubs();
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?latitude=25.2&longitude=55.3", MapCapturesWebApplicationFactory.ApiKey),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "body was: {0}\nrecent log:\n{1}",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            string.Join('\n', CapturedLogSink.Shared.Lines.TakeLast(15)));
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        response.Headers.GetValues("X-Map-Capture-Resolved-Location").Should().ContainSingle("25.2,55.3");
        response.Headers.GetValues("X-Map-Capture-Mode").Should().ContainSingle("satellite");
        response.Headers.GetValues("X-Map-Capture-Width").Should().ContainSingle("1252");
        response.Headers.GetValues("X-Map-Capture-Height").Should().ContainSingle("1252");
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken))
            .Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ShouldParseHyphenatedModeAndOverlays()
    {
        DefaultStubs();
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?latitude=25.2&longitude=55.3&mode=buildings-only&overlays=building-footprints",
                MapCapturesWebApplicationFactory.ApiKey),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await factory.Renderer.Received(1).RenderAsync(
            Arg.Is<MapImageRequest>(r =>
                r != null &&
                r.Mode == MapCaptureMode.BuildingsOnly
                && r.Overlays.SequenceEqual(new[] { MapCaptureOverlay.BuildingFootprints })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldReturn400_ForAnUnknownMode()
    {
        DefaultStubs();
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?latitude=25.2&longitude=55.3&mode=terrain", MapCapturesWebApplicationFactory.ApiKey),
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "body was: {0}", body);
    }

    [Fact]
    public async Task ShouldReturn400_WhenOnlyLatitudeIsSupplied()
    {
        DefaultStubs();
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?latitude=25.2", MapCapturesWebApplicationFactory.ApiKey),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShouldReturn404_WhenTheLocationIsNotFound()
    {
        DefaultStubs();
        factory.Geocoding.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<GeocodingCandidate>>([]));
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?location=nowhere", MapCapturesWebApplicationFactory.ApiKey),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Location not found");
    }

    [Fact]
    public async Task ShouldReturn422_ForSiteBoundary()
    {
        DefaultStubs();
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?latitude=25.2&longitude=55.3&overlays=site-boundary",
                MapCapturesWebApplicationFactory.ApiKey),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("site-boundary");
    }

    [Fact]
    public async Task ShouldReturn502_WhenTheImageryProviderFails()
    {
        DefaultStubs();
        factory.Renderer.RenderAsync(Arg.Any<MapImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(MapImageResult.Failure(MapCaptureFailureReason.ProviderUnavailable, "upstream down"));
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?latitude=25.2&longitude=55.3", MapCapturesWebApplicationFactory.ApiKey),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Upstream provider unavailable");
    }

    [Fact]
    public async Task ShouldIncludeTraceId_InEveryProblemDetails()
    {
        DefaultStubs();
        using var client = ClientWithKey();

        var response = await client.SendAsync(
            Get("?latitude=25.2&longitude=55.3&mode=terrain", MapCapturesWebApplicationFactory.ApiKey),
            TestContext.Current.CancellationToken);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("traceId");
    }
}

/// <summary>The rate-limit policy must partition per authenticated caller, not per IP.</summary>
[Collection(DatabaseSerialGroup.Name)]
public sealed class MapCaptureRateLimitTests(RateLimitedMapCapturesFactory factory)
    : IClassFixture<RateLimitedMapCapturesFactory>
{
    private const string Url = "/api/v1/map-captures";

    private static HttpRequestMessage Get(string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url + "?latitude=25.2&longitude=55.3");
        request.Headers.Add("X-Api-Key", key);
        return request;
    }

    [Fact]
    public async Task ShouldReturn429_OnTheThirdRequest_ButNotForAnotherKey()
    {
        factory.Renderer.RenderAsync(Arg.Any<MapImageRequest>(), Arg.Any<CancellationToken>())
            .Returns(MapImageResult.Success([1, 2, 3], 1252, 1252));
        using var client = factory.CreateClient();

        var first = await client.SendAsync(
            Get(MapCapturesWebApplicationFactory.ApiKey), TestContext.Current.CancellationToken);
        var second = await client.SendAsync(
            Get(MapCapturesWebApplicationFactory.ApiKey), TestContext.Current.CancellationToken);
        var third = await client.SendAsync(
            Get(MapCapturesWebApplicationFactory.ApiKey), TestContext.Current.CancellationToken);
        var other = await client.SendAsync(
            Get(MapCapturesWebApplicationFactory.SecondApiKey), TestContext.Current.CancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK, "body was: {0}", await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        other.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "body was: {0}\nrecent log:\n{1}",
            await other.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            string.Join('\n', CapturedLogSink.Shared.Lines.TakeLast(20)));
    }
}
