using System.Net;
using System.Text;
using AskLucy.Application.Locations;
using AskLucy.Infrastructure.Geocoding;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Geocoding;

/// <summary>
/// specs/037-location-query-resolution T013 — <see cref="NominatimGeocodingProvider"/> parses
/// Nominatim search results into <see cref="GeocodingCandidate"/> records, drops malformed rows,
/// and maps all HTTP/network/JSON failures to <see cref="GeocodingProviderUnavailableException"/>.
/// </summary>
public sealed class NominatimGeocodingProviderTests
{
    private const string NominatimJson = """
        [
          {"display_name":"Dubai, UAE","lat":"25.2048","lon":"55.2708","importance":0.85},
          {"display_name":"Dubai Municipality","lat":"25.1972","lon":"55.2796","importance":0.62}
        ]
        """;

    private static NominatimGeocodingProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = null };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Geocoding").Returns(httpClient);
        var options = Options.Create(new GeocodingOptions());
        return new NominatimGeocodingProvider(factory, options, NullLogger<NominatimGeocodingProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SearchAsync_ShouldReturnMappedCandidates_WhenNominatimReturnsResults()
    {
        var provider = CreateProvider(_ => JsonResponse(NominatimJson), out var handler);

        var results = await provider.SearchAsync("Dubai", CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].LocationName.Should().Be("Dubai, UAE");
        results[0].Latitude.Should().Be(25.2048);
        results[0].Longitude.Should().Be(55.2708);
        results[0].Importance.Should().Be(0.85);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Contain("search?q=Dubai");
        handler.LastRequest.Headers.UserAgent.ToString().Should().Contain("AskLucy");
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmpty_WhenNominatimReturnsEmptyArray()
    {
        var provider = CreateProvider(_ => JsonResponse("[]"), out _);

        var results = await provider.SearchAsync("nowhere-special", CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ShouldDropRows_WhenLatOrLonCannotBeParsed()
    {
        var malformedJson = """
            [
              {"display_name":"Good Place","lat":"25.0","lon":"55.0","importance":0.8},
              {"display_name":"Bad Lat","lat":"NOT_A_NUMBER","lon":"55.0","importance":0.7},
              {"display_name":"Bad Lon","lat":"25.0","lon":"INVALID","importance":0.6}
            ]
            """;
        var provider = CreateProvider(_ => JsonResponse(malformedJson), out _);

        var results = await provider.SearchAsync("test", CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].LocationName.Should().Be("Good Place");
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
    public async Task SearchAsync_ShouldEscapeQueryInUrl()
    {
        var provider = CreateProvider(_ => JsonResponse("[]"), out var handler);

        await provider.SearchAsync("Al Safa Park 2", CancellationToken.None);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Contain("Al%20Safa%20Park%202");
    }
}
