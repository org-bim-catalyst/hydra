using AskLucy.Application.Abstractions;
using AskLucy.Application.Weather;
using AskLucy.Application.Weather.Queries.GetCurrentWeather;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Application.Tests.Weather;

public sealed class GetCurrentWeatherQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnTheProviders_Snapshot()
    {
        var provider = Substitute.For<IWeatherProvider>();
        var observedAtUtc = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
        var snapshot = new WeatherSnapshotDto("London, United Kingdom", 15.0, WeatherCondition.Cloudy, true, observedAtUtc);
        provider.GetCurrentWeatherAsync(51.5074, -0.1278, Arg.Any<CancellationToken>()).Returns(snapshot);

        var handler = new GetCurrentWeatherQueryHandler(provider);
        var result = await handler.Handle(new GetCurrentWeatherQuery(51.5074, -0.1278), CancellationToken.None);

        result.Should().Be(snapshot);
    }

    [Fact]
    public async Task Handle_ShouldPropagateWeatherProviderUnavailableException_ForTheMiddlewareToMap()
    {
        var provider = Substitute.For<IWeatherProvider>();
        provider.GetCurrentWeatherAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Throws(new WeatherProviderUnavailableException("The weather service timed out."));

        var handler = new GetCurrentWeatherQueryHandler(provider);
        var act = () => handler.Handle(new GetCurrentWeatherQuery(0, 0), CancellationToken.None);

        await act.Should().ThrowAsync<WeatherProviderUnavailableException>();
    }
}
