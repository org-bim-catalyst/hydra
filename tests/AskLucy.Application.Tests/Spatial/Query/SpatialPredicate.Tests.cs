using AskLucy.Application.Spatial.Query;

namespace AskLucy.Application.Tests.Spatial.Query;

/// <summary>
/// specs/095 — unit tests for <see cref="SpatialPredicate"/> construction and the distance
/// unit conversion helper.
/// </summary>
public class SpatialPredicateTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        var predicate = new SpatialPredicate(
            Relationship: SpatialRelationship.Within,
            ReferenceKind: SpatialReferenceKind.NamedFeature,
            ReferenceName: "test-feature",
            DistanceMeters: 100,
            DistanceUnit: "m");

        Assert.Equal(SpatialRelationship.Within, predicate.Relationship);
        Assert.Equal(SpatialReferenceKind.NamedFeature, predicate.ReferenceKind);
        Assert.Equal("test-feature", predicate.ReferenceName);
        Assert.Equal(100, predicate.DistanceMeters);
        Assert.Equal("m", predicate.DistanceUnit);
    }

    [Theory]
    [InlineData("m", 50, 50)]
    [InlineData("km", 1, 1000)]
    [InlineData("ft", 3.28084, 1)]
    [InlineData("mi", 0.000621371, 1)]
    [InlineData(null, 25, 25)]
    public void ToMetres_ConvertsToMetres(string? unit, double value, double expected)
    {
        var metres = SpatialPredicate.ToMetres(value, unit);

        Assert.Equal(expected, metres, 3);
    }
}
