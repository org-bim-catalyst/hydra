using AskLucy.Application.Weather.Queries.GetCurrentWeather;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Weather;

public sealed class GetCurrentWeatherQueryValidatorTests
{
    private readonly GetCurrentWeatherQueryValidator _validator = new();

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(51.5074, -0.1278)]
    [InlineData(0, 0)]
    public void Validate_ShouldPass_ForInRangeCoordinates(double latitude, double longitude)
    {
        var result = _validator.Validate(new GetCurrentWeatherQuery(latitude, longitude));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(90.1, 0)]
    [InlineData(-90.1, 0)]
    public void Validate_ShouldFail_ForOutOfRangeLatitude(double latitude, double longitude)
    {
        var result = _validator.Validate(new GetCurrentWeatherQuery(latitude, longitude));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(GetCurrentWeatherQuery.Latitude));
    }

    [Theory]
    [InlineData(0, 180.1)]
    [InlineData(0, -180.1)]
    public void Validate_ShouldFail_ForOutOfRangeLongitude(double latitude, double longitude)
    {
        var result = _validator.Validate(new GetCurrentWeatherQuery(latitude, longitude));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(GetCurrentWeatherQuery.Longitude));
    }
}
