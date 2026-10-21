using AskLucy.Application.MapCaptures;
using AskLucy.Application.MapCaptures.Queries.CaptureMap;
using FluentValidation.TestHelper;
using Xunit;

namespace AskLucy.Application.Tests.MapCaptures;

/// <summary>specs/083-viewer-map-capture T010 — the validation rules from data-model.md.</summary>
public sealed class CaptureMapQueryValidatorTests
{
    private readonly CaptureMapQueryValidator _validator =
        new(Microsoft.Extensions.Options.Options.Create(new MapCaptureOptions()));

    private static CaptureMapQuery Query(
        string? location = null, double? latitude = null, double? longitude = null,
        int? viewSizeMetres = null, string? mode = null, string? overlays = null) =>
        new(location, latitude, longitude, viewSizeMetres, mode, overlays, "pipeline");

    [Fact]
    public void ShouldNotHaveError_ForCoordinates()
    {
        _validator.TestValidate(Query(latitude: 25.2, longitude: 55.3))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ShouldNotHaveError_ForAPlaceName()
    {
        _validator.TestValidate(Query(location: "Al Safa Park 2"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ShouldHaveError_WhenNeitherLocationFormIsSupplied()
    {
        _validator.TestValidate(Query())
            .ShouldHaveValidationErrorFor(q => q.Location);
    }

    [Fact]
    public void ShouldHaveError_WhenOnlyLatitudeIsSupplied()
    {
        _validator.TestValidate(Query(latitude: 25.2))
            .ShouldHaveValidationErrorFor(q => q.Longitude);
    }

    [Fact]
    public void ShouldHaveError_WhenOnlyLongitudeIsSupplied()
    {
        _validator.TestValidate(Query(longitude: 55.3))
            .ShouldHaveValidationErrorFor(q => q.Latitude);
    }

    [Theory]
    [InlineData(-91)]
    [InlineData(91)]
    public void ShouldHaveError_WhenLatitudeIsOutOfRange(double latitude)
    {
        _validator.TestValidate(Query(latitude: latitude, longitude: 55.3))
            .ShouldHaveValidationErrorFor(q => q.Latitude);
    }

    [Theory]
    [InlineData(-181)]
    [InlineData(181)]
    public void ShouldHaveError_WhenLongitudeIsOutOfRange(double longitude)
    {
        _validator.TestValidate(Query(latitude: 25.2, longitude: longitude))
            .ShouldHaveValidationErrorFor(q => q.Longitude);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(20001)]
    public void ShouldHaveError_WhenViewSizeIsOutOfRange(int viewSizeMetres)
    {
        _validator.TestValidate(Query(latitude: 25.2, longitude: 55.3, viewSizeMetres: viewSizeMetres))
            .ShouldHaveValidationErrorFor(q => q.ViewSizeMetres);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(20000)]
    public void ShouldNotHaveError_AtViewSizeBoundaries(int viewSizeMetres)
    {
        _validator.TestValidate(Query(latitude: 25.2, longitude: 55.3, viewSizeMetres: viewSizeMetres))
            .ShouldNotHaveValidationErrorFor(q => q.ViewSizeMetres);
    }

    [Theory]
    [InlineData("terrain")]
    [InlineData("BuildingsOnly")]
    public void ShouldHaveError_WhenModeIsNotAWireValue(string mode)
    {
        _validator.TestValidate(Query(latitude: 25.2, longitude: 55.3, mode: mode))
            .ShouldHaveValidationErrorFor(q => q.Mode);
    }

    [Fact]
    public void ShouldHaveError_WhenAnOverlayIsUnknown()
    {
        _validator.TestValidate(Query(latitude: 25.2, longitude: 55.3, overlays: "building-footprints,contours"))
            .ShouldHaveValidationErrorFor(q => q.Overlays);
    }

    [Fact]
    public void ShouldNotHaveError_ForSiteBoundary_SoTheHandlerCanNameIt()
    {
        _validator.TestValidate(Query(latitude: 25.2, longitude: 55.3, overlays: "site-boundary"))
            .ShouldNotHaveAnyValidationErrors();
    }
}
