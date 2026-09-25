using System.Net;
using System.Net.Http.Headers;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Boundaries;

/// <summary>
/// The ESRI imagery fallback uses the ArcGIS API key when one is configured, and never loses the
/// image because of a bad key.
/// </summary>
public sealed class EsriSatelliteImageProviderTests
{
    private static readonly GeoPoint BurJuman = new(25.2545229, 55.303495);

    private static EsriSatelliteImageProvider CreateProvider(
        List<HttpRequestMessage> requests, string? apiKey, Func<HttpRequestMessage, HttpStatusCode>? statusFor = null)
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request);
            var status = statusFor?.Invoke(request) ?? HttpStatusCode.OK;
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(status == HttpStatusCode.OK ? "image/png" : "text/plain");
            return response;
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("EsriWorldImagery").Returns(_ => new HttpClient(handler));

        return new EsriSatelliteImageProvider(
            factory,
            Options.Create(new EsriOptions { ApiKey = apiKey }),
            NullLogger<EsriSatelliteImageProvider>.Instance);
    }

    [Fact]
    public async Task FetchAsync_ShouldUseTheKeylessEndpoint_WhenNoApiKeyIsConfigured()
    {
        var requests = new List<HttpRequestMessage>();
        var provider = CreateProvider(requests, apiKey: null);

        var image = await provider.FetchAsync(BurJuman, 300, TestContext.Current.CancellationToken);

        image.Should().NotBeNull();
        requests.Should().ContainSingle();
        requests[0].RequestUri!.Host.Should().Be("services.arcgisonline.com");
        requests[0].Headers.Contains("X-Esri-Authorization").Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_ShouldSendTheApiKeyInAHeader_NeverInTheUrl()
    {
        var requests = new List<HttpRequestMessage>();
        var provider = CreateProvider(requests, apiKey: "test-esri-key");

        var image = await provider.FetchAsync(BurJuman, 300, TestContext.Current.CancellationToken);

        image.Should().NotBeNull();
        requests.Should().ContainSingle();
        requests[0].RequestUri!.Host.Should().Be("ibasemaps-api.arcgis.com");
        requests[0].RequestUri!.ToString().Should().NotContain("test-esri-key", "URLs end up in request logs");
        requests[0].Headers.GetValues("X-Esri-Authorization").Should().Equal("Bearer test-esri-key");
    }

    [Fact]
    public async Task FetchAsync_ShouldFallBackToKeyless_WhenTheKeyIsRejected()
    {
        var requests = new List<HttpRequestMessage>();
        var provider = CreateProvider(requests, apiKey: "expired-key",
            statusFor: r => r.RequestUri!.Host == "ibasemaps-api.arcgis.com" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);

        var image = await provider.FetchAsync(BurJuman, 300, TestContext.Current.CancellationToken);

        image.Should().NotBeNull();
        requests.Select(r => r.RequestUri!.Host).Should().Equal("ibasemaps-api.arcgis.com", "services.arcgisonline.com");
        requests[1].Headers.Contains("X-Esri-Authorization").Should().BeFalse("the rejected key isn't sent to the keyless endpoint");
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnNull_WhenBothEndpointsFail()
    {
        var requests = new List<HttpRequestMessage>();
        var provider = CreateProvider(requests, apiKey: "test-esri-key", statusFor: _ => HttpStatusCode.ServiceUnavailable);

        var image = await provider.FetchAsync(BurJuman, 300, TestContext.Current.CancellationToken);

        image.Should().BeNull();
        requests.Should().HaveCount(2);
    }
}
