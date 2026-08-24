using System.Net;
using System.Text;
using AskLucy.Application.Locations;
using AskLucy.Infrastructure.Geocoding;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Geocoding;

/// <summary>
/// Google Maps Geocoding API provider — parses results into
/// <see cref="GeocodingCandidate"/> records, derives importance from <c>location_type</c>,
/// and maps all HTTP/non-OK-status/JSON failures to
/// <see cref="GeocodingProviderUnavailableException"/>.
/// </summary>
public sealed class GoogleMapsGeocodingProviderTests
{
    private const string OkJson = """
        {
          "status": "OK",
          "results": [
            {
              "formatted_address": "Al Safa Park - Al Wasl - Dubai - UAE",
              "geometry": {
                "location": { "lat": 25.2048, "lng": 55.2708 },
                "location_type": "GEOMETRIC_CENTER"
              }
            },
            {
              "formatted_address": "Al Safa Park 2, Dubai, UAE",
              "geometry": {
                "location": { "lat": 25.2065, "lng": 55.2721 },
                "location_type": "APPROXIMATE"
              }
            }
          ]
        }
        """;

    private static GoogleMapsGeocodingProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = null };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Geocoding").Returns(httpClient);
        var options = Options.Create(new GoogleMapsGeocodingOptions { GoogleMapsApiKey = "test-key" });
        return new GoogleMapsGeocodingProvider(factory, options, NullLogger<GoogleMapsGeocodingProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SearchAsync_ShouldReturnMappedCandidates_WhenGoogleReturnsOk()
    {
        var provider = CreateProvider(_ => JsonResponse(OkJson), out _);

        var results = await provider.SearchAsync("Al Safa Park 2", CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].LocationName.Should().Be("Al Safa Park - Al Wasl - Dubai - UAE");
        results[0].Latitude.Should().Be(25.2048);
        results[0].Longitude.Should().Be(55.2708);
        results[0].Importance.Should().Be(0.60); // GEOMETRIC_CENTER
        results[1].Importance.Should().Be(0.40); // APPROXIMATE
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmpty_WhenGoogleReturnsZeroResults()
    {
        var provider = CreateProvider(_ => JsonResponse("""{"status":"ZERO_RESULTS","results":[]}"""), out _);

        var results = await provider.SearchAsync("nowhere-special", CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ShouldMapLocationTypeToImportanceCorrectly()
    {
        var json = """
            {
              "status": "OK",
              "results": [
                {
                  "formatted_address": "Rooftop Place",
                  "geometry": { "location": { "lat": 1.0, "lng": 1.0 }, "location_type": "ROOFTOP" }
                },
                {
                  "formatted_address": "Interpolated Place",
                  "geometry": { "location": { "lat": 2.0, "lng": 2.0 }, "location_type": "RANGE_INTERPOLATED" }
                },
                {
                  "formatted_address": "Geometric Place",
                  "geometry": { "location": { "lat": 3.0, "lng": 3.0 }, "location_type": "GEOMETRIC_CENTER" }
                },
                {
                  "formatted_address": "Approx Place",
                  "geometry": { "location": { "lat": 4.0, "lng": 4.0 }, "location_type": "APPROXIMATE" }
                },
                {
                  "formatted_address": "Unknown Place",
                  "geometry": { "location": { "lat": 5.0, "lng": 5.0 }, "location_type": "SOMETHING_NEW" }
                }
              ]
            }
            """;
        var provider = CreateProvider(_ => JsonResponse(json), out _);

        var results = await provider.SearchAsync("test", CancellationToken.None);

        results.Should().HaveCount(5);
        results[0].Importance.Should().Be(0.90); // ROOFTOP
        results[1].Importance.Should().Be(0.70); // RANGE_INTERPOLATED
        results[2].Importance.Should().Be(0.60); // GEOMETRIC_CENTER
        results[3].Importance.Should().Be(0.40); // APPROXIMATE
        results[4].Importance.Should().Be(0.50); // unknown → default
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowGeocodingProviderUnavailableException_OnHttpError()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), out _);

        var act = async () => await provider.SearchAsync("Dubai", CancellationToken.None);

        await act.Should().ThrowAsync<GeocodingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowGeocodingProviderUnavailableException_OnMalformedJson()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("this is not json", Encoding.UTF8, "application/json")
        }, out _);

        var act = async () => await provider.SearchAsync("Dubai", CancellationToken.None);

        await act.Should().ThrowAsync<GeocodingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowGeocodingProviderUnavailableException_WhenStatusIsRequestDenied()
    {
        var provider = CreateProvider(_ => JsonResponse("""{"status":"REQUEST_DENIED","results":[]}"""), out _);

        var act = async () => await provider.SearchAsync("Dubai", CancellationToken.None);

        await act.Should().ThrowAsync<GeocodingProviderUnavailableException>();
    }

    [Fact]
    public async Task SearchAsync_ShouldEscapeQueryInUrl()
    {
        var provider = CreateProvider(_ => JsonResponse("""{"status":"ZERO_RESULTS","results":[]}"""), out var handler);

        await provider.SearchAsync("Al Safa Park 2", CancellationToken.None);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Contain("Al%20Safa%20Park%202");
    }

    [Fact]
    public async Task SearchAsync_ShouldIncludeApiKeyInUrl()
    {
        var provider = CreateProvider(_ => JsonResponse("""{"status":"ZERO_RESULTS","results":[]}"""), out var handler);

        await provider.SearchAsync("Dubai", CancellationToken.None);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Contain("key=test-key");
    }

    // specs/038-viewer-poi-zoom — viewport and locationType tests.

    [Fact]
    public async Task SearchAsync_ShouldPopulateViewport_WhenGoogleReturnsViewportField()
    {
        const string json = """
            {
              "status": "OK",
              "results": [
                {
                  "formatted_address": "Dubai Mall, Dubai, UAE",
                  "geometry": {
                    "location": { "lat": 25.1972, "lng": 55.2796 },
                    "location_type": "ROOFTOP",
                    "viewport": {
                      "northeast": { "lat": 25.1985, "lng": 55.2812 },
                      "southwest": { "lat": 25.1958, "lng": 55.2779 }
                    }
                  }
                }
              ]
            }
            """;
        var provider = CreateProvider(_ => JsonResponse(json), out _);

        var results = await provider.SearchAsync("Dubai Mall", CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Viewport.Should().NotBeNull();
        results[0].Viewport!.NortheastLat.Should().Be(25.1985);
        results[0].Viewport!.NortheastLng.Should().Be(55.2812);
        results[0].Viewport!.SouthwestLat.Should().Be(25.1958);
        results[0].Viewport!.SouthwestLng.Should().Be(55.2779);
    }

    [Fact]
    public async Task SearchAsync_ShouldSetViewportToNull_WhenViewportAbsentFromResponse()
    {
        const string json = """
            {
              "status": "OK",
              "results": [
                {
                  "formatted_address": "Dubai, UAE",
                  "geometry": {
                    "location": { "lat": 25.2048, "lng": 55.2708 },
                    "location_type": "APPROXIMATE"
                  }
                }
              ]
            }
            """;
        var provider = CreateProvider(_ => JsonResponse(json), out _);

        var results = await provider.SearchAsync("Dubai", CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Viewport.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_ShouldMapLocationTypeFieldOnCandidate()
    {
        var provider = CreateProvider(_ => JsonResponse(OkJson), out _);

        var results = await provider.SearchAsync("Al Safa Park", CancellationToken.None);

        results[0].LocationType.Should().Be("GEOMETRIC_CENTER");
        results[1].LocationType.Should().Be("APPROXIMATE");
    }
}
