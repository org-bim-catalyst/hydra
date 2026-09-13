using AskLucy.Application.Buildings.Queries.GetSiteBuildings;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace AskLucy.Application.Tests.Buildings;

/// <summary>specs/052-solar-analysis contracts/building-footprints-endpoint.md — latitude
/// -90..90, longitude -180..180, radius 50..1000 (T035).</summary>
public sealed class GetSiteBuildingsQueryValidatorTests
{
    private readonly GetSiteBuildingsQueryValidator _validator = new();

    [Fact]
    public void ShouldNotHaveError_ForAValidQuery()
    {
        var result = _validator.TestValidate(new GetSiteBuildingsQuery(25.2, 55.3, 200));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-91)]
    [InlineData(91)]
    public void ShouldHaveError_WhenLatitudeIsOutOfRange(double latitude)
    {
        var result = _validator.TestValidate(new GetSiteBuildingsQuery(latitude, 55.3, 200));
        result.ShouldHaveValidationErrorFor(q => q.Latitude);
    }

    [Theory]
    [InlineData(-181)]
    [InlineData(181)]
    public void ShouldHaveError_WhenLongitudeIsOutOfRange(double longitude)
    {
        var result = _validator.TestValidate(new GetSiteBuildingsQuery(25.2, longitude, 200));
        result.ShouldHaveValidationErrorFor(q => q.Longitude);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(1001)]
    public void ShouldHaveError_WhenRadiusIsOutOfRange(int radius)
    {
        var result = _validator.TestValidate(new GetSiteBuildingsQuery(25.2, 55.3, radius));
        result.ShouldHaveValidationErrorFor(q => q.RadiusMetres);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(1000)]
    public void ShouldNotHaveError_AtRadiusBoundaries(int radius)
    {
        var result = _validator.TestValidate(new GetSiteBuildingsQuery(25.2, 55.3, radius));
        result.ShouldNotHaveValidationErrorFor(q => q.RadiusMetres);
    }
}
