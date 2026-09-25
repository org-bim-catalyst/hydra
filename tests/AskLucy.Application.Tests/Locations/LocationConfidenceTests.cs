using AskLucy.Application.Locations;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Locations;

public sealed class LocationConfidenceTests
{
    [Theory]
    [InlineData("ROOFTOP", BoundaryConfidenceLevel.High)]
    [InlineData("RANGE_INTERPOLATED", BoundaryConfidenceLevel.Medium)]
    [InlineData("GEOMETRIC_CENTER", BoundaryConfidenceLevel.Medium)]
    [InlineData("APPROXIMATE", BoundaryConfidenceLevel.Low)]
    public void Classify_ShouldReadTheGeocodersPrecisionCode(string locationType, BoundaryConfidenceLevel expected) =>
        LocationConfidence.Classify(locationType).Should().Be(expected);

    // Nominatim reports no precision code, and its importance scores a correctly-matched local
    // park at ~0.08 — the level must not be read from that number, so an unstated precision is
    // Medium rather than Low.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SOMETHING_NEW")]
    public void Classify_ShouldBeMedium_WhenTheGeocoderStatesNoKnownPrecision(string? locationType) =>
        LocationConfidence.Classify(locationType).Should().Be(BoundaryConfidenceLevel.Medium);
}
