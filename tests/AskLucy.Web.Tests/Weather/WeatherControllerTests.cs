using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Weather;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Web.Tests.Weather;

/// <summary>
/// Same no-live-database pattern as <see cref="Consent.CookieConsentControllerTests"/> for
/// auth/validation-gate coverage; the 200/502 cases substitute a fake <see cref="IWeatherProvider"/>
/// via <c>ConfigureTestServices</c> so they never make a real call to Open-Meteo/Nominatim.
/// </summary>
public sealed class WeatherControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private HttpClient CreateClientWithProvider(IWeatherProvider provider)
    {
        var customFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWeatherProvider>();
                services.AddScoped(_ => provider);
            }));
        return customFactory.CreateClient();
    }

    private static void Authorize(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

    [Fact]
    public async Task GetCurrent_ShouldReturn401_WhenNoBearerTokenIsProvided()
    {
        var response = await _client.GetAsync("/api/v1/weather/current?latitude=51.5074&longitude=-0.1278");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrent_ShouldReturn400ProblemDetails_ForAnOutOfRangeLatitude()
    {
        var client = _client;
        Authorize(client);

        var response = await client.GetAsync("/api/v1/weather/current?latitude=999&longitude=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetCurrent_ShouldReturn200_WithTheProvidersSnapshot()
    {
        var provider = Substitute.For<IWeatherProvider>();
        var observedAtUtc = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
        provider.GetCurrentWeatherAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new WeatherSnapshotDto("London, United Kingdom", 15.0, WeatherCondition.Cloudy, true, observedAtUtc));
        var client = CreateClientWithProvider(provider);
        Authorize(client);

        var response = await client.GetAsync("/api/v1/weather/current?latitude=51.5074&longitude=-0.1278");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Matches the API's own JSON configuration (JsonEnumSerializationTests.cs) — enums
        // round-trip as strings, not System.Text.Json's numeric-ordinal default.
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var body = await response.Content.ReadFromJsonAsync<WeatherSnapshotDto>(jsonOptions);
        body!.LocationName.Should().Be("London, United Kingdom");
        body.Condition.Should().Be(WeatherCondition.Cloudy);
    }

    [Fact]
    public async Task GetCurrent_ShouldReturn502ProblemDetails_WhenTheProviderIsUnavailable()
    {
        var provider = Substitute.For<IWeatherProvider>();
        provider.GetCurrentWeatherAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Throws(new WeatherProviderUnavailableException("The weather service timed out."));
        var client = CreateClientWithProvider(provider);
        Authorize(client);

        var response = await client.GetAsync("/api/v1/weather/current?latitude=51.5074&longitude=-0.1278");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
