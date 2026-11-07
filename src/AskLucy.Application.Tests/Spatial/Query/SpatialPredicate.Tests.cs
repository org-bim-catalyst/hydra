using AskLucy.Application.Spatial.Query;
using Xunit;

namespace AskLucy.Application.Tests.Spatial.Query;

/// <summary>
/// specs/095 — unit tests for <see cref="SpatialPredicate"/> parsing and validation.
/// </summary>
public class SpatialPredicateTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        // Act
        var predicate = new SpatialPredicate(
            Relationship: SpatialRelationship.Within,
            ReferenceKind: SpatialReferenceKind.NamedFeature,
            ReferenceName: "test-feature",
            DistanceMeters: 100,
            DistanceUnit: "m");

        // Assert
        Assert.Equal(SpatialRelationship.Within, predicate.Relationship);
        Assert.Equal(SpatialReferenceKind.NamedFeature, predicate.ReferenceKind);
        Assert.Equal("test-feature", predicate.ReferenceName);
        Assert.Equal(100, predicate.DistanceMeters);
        Assert.Equal("m", predicate.DistanceUnit);
    }

    [Fact]
    public void ToMetres_m_returnsSameValue()
    {
        // Act
        var metres = SpatialPredicate.ToMetres(50, "m");

        // Assert
        Assert.Equal(50, metres);
    }

    [Fact]
    public void ToMetres_km_converts()
    {
        // Act
        var metres = SpatialPredicate.ToMetres(1, "km");

        // Assert
        Assert.Equal(1000, metres);
    }

    [Fact]
    public void ToMetres_ft_converts()
    {
        // Act
        var metres = SpatialPredicate.ToMetres(3.28084, "ft");

        // Assert
        Assert.Equal(1, metres); // 3.28084 ft ≈ 1 m
    }

    [Fact]
    public void ToMetres_mi_converts()
    {
        // Act
        var metres = SpatialPredicate.ToMetres(0.000621371, "mi");

        // Assert
        Assert.Equal(1, metres); // 0.000621371 mi ≈ 1 m
    }
}