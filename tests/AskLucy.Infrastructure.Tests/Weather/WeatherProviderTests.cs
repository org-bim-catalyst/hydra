using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Tests.Ai;
using AskLucy.Infrastructure.Weather;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Weather;

public sealed class WeatherProviderTests
{
    private const string ForecastJson = """
        {"current":{"temperature_2m":15.0,"weather_code":3,"is_day":1,"wind_speed_10m":10.0}}
        """;

    private const string NominatimJson = """
        {"display_name":"London, Greater London, England, United Kingdom","address":{"city":"London","country":"United Kingdom"}}
        """;

    private WeatherProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Weather").Returns(httpClient);

        var options = Options.Create(new WeatherOptions());
        return new WeatherProvider(factory, options, Substitute.For<ILogger<WeatherProvider>>());
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetCurrentWeatherAsync_ShouldReturnASnapshot_CombiningForecastAndReverseGeocodedLocationName()
    {
        var provider = CreateProvider(request =>
            request.RequestUri!.Host.Contains("open-meteo")
                ? JsonResponse(ForecastJson)
                : JsonResponse(NominatimJson), out _);

        var result = await provider.GetCurrentWeatherAsync(51.5074, -0.1278, CancellationToken.None);

        result.LocationName.Should().Be("London, United Kingdom");
        result.TemperatureCelsius.Should().Be(15.0);
        result.Condition.Should().Be(WeatherCondition.Cloudy);
        result.IsDaytime.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, WeatherCondition.Clear)]
    [InlineData(1, WeatherCondition.PartlyCloudy)]
    [InlineData(2, WeatherCondition.PartlyCloudy)]
    [InlineData(3, WeatherCondition.Cloudy)]
    [InlineData(45, WeatherCondition.Fog)]
    [InlineData(61, WeatherCondition.Rain)]
    [InlineData(80, WeatherCondition.Rain)]
    [InlineData(71, WeatherCondition.Snow)]
    [InlineData(85, WeatherCondition.Snow)]
    [InlineData(95, WeatherCondition.Thunderstorm)]
    [InlineData(99, WeatherCondition.Thunderstorm)]
    public async Task GetCurrentWeatherAsync_ShouldMapEachWmoWeatherCode_ToTheExpectedCondition(int weatherCode, WeatherCondition expected)
    {
        var forecastJson = $$$"""{"current":{"temperature_2m":10.0,"weather_code":{{{weatherCode}}},"is_day":0,"wind_speed_10m":5.0}}""";
        var provider = CreateProvider(request =>
            request.RequestUri!.Host.Contains("open-meteo")
                ? JsonResponse(forecastJson)
                : JsonResponse(NominatimJson), out _);

        var result = await provider.GetCurrentWeatherAsync(0, 0, CancellationToken.None);

        result.Condition.Should().Be(expected);
        result.IsDaytime.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_ShouldReportWindy_RegardlessOfWeatherCode_WhenWindSpeedIsHigh()
    {
        var forecastJson = """{"current":{"temperature_2m":10.0,"weather_code":0,"is_day":1,"wind_speed_10m":45.0}}""";
        var provider = CreateProvider(request =>
            request.RequestUri!.Host.Contains("open-meteo")
                ? JsonResponse(forecastJson)
                : JsonResponse(NominatimJson), out _);

        var result = await provider.GetCurrentWeatherAsync(0, 0, CancellationToken.None);

        result.Condition.Should().Be(WeatherCondition.Windy);
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_ShouldThrowWeatherProviderUnavailableException_WhenTheForecastCallFails()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), out _);

        var act = () => provider.GetCurrentWeatherAsync(0, 0, CancellationToken.None);

        await act.Should().ThrowAsync<WeatherProviderUnavailableException>();
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_ShouldThrowWeatherProviderUnavailableException_OnTimeout()
    {
        var provider = CreateProvider(_ => throw new TaskCanceledException("Timed out"), out _);

        var act = () => provider.GetCurrentWeatherAsync(0, 0, CancellationToken.None);

        await act.Should().ThrowAsync<WeatherProviderUnavailableException>();
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_ShouldFallBackToUnknownLocation_WhenReverseGeocodingFails_WithoutFailingTheWholeLookup()
    {
        var provider = CreateProvider(request =>
            request.RequestUri!.Host.Contains("open-meteo")
                ? JsonResponse(ForecastJson)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), out _);

        var result = await provider.GetCurrentWeatherAsync(0, 0, CancellationToken.None);

        result.LocationName.Should().Be("Unknown location");
        result.TemperatureCelsius.Should().Be(15.0);
    }
}
