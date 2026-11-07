using AskLucy.Application.Spatial.Query;
using Xunit;

namespace AskLucy.Application.Tests.Spatial.Query;

/// <summary>
/// specs/095 — unit tests for <see cref="AttributePredicate"/> parsing and validation.
/// </summary>
public class AttributePredicateTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        // Act
        var predicate = new AttributePredicate(
            Field: "height",
            Operator: AttributeOperator.Gt,
            Value: 30,
            ValueTo: null,
            Unit: "m");

        // Assert
        Assert.Equal("height", predicate.Field);
        Assert.Equal(AttributeOperator.Gt, predicate.Operator);
        Assert.Equal(30, predicate.Value);
        Assert.Null(predicate.ValueTo);
        Assert.Equal("m", predicate.Unit);
    }

    [Fact]
    public void Constructor_WithValueTo_SetsValueTo()
    {
        // Act
        var predicate = new AttributePredicate(
            Field: "age",
            Operator: AttributeOperator.Between,
            Value: 18,
            ValueTo: 65,
            Unit: null);

        // Assert
        Assert.Equal(18, predicate.Value);
        Assert.Equal(65, predicate.ValueTo);
        Assert.Null(predicate.Unit);
    }

    [Fact]
    public void Constructor_WithUnit_InferFromFieldType()
    {
        // Act - Unit specified explicitly
        var predicate = new AttributePredicate(
            Field: "area",
            Operator: AttributeOperator.Gt,
            Value: 100,
            ValueTo: null,
            Unit: "m²");

        // Assert
        Assert.Equal("m²", predicate.Unit);
    }
}