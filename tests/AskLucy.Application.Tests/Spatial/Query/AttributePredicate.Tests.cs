using AskLucy.Application.Spatial.Query;

namespace AskLucy.Application.Tests.Spatial.Query;

/// <summary>specs/095 — unit tests for <see cref="AttributePredicate"/> construction.</summary>
public class AttributePredicateTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        var predicate = new AttributePredicate(
            Field: "height",
            Operator: AttributeOperator.Gt,
            Value: 30,
            ValueTo: null,
            Unit: "m");

        Assert.Equal("height", predicate.Field);
        Assert.Equal(AttributeOperator.Gt, predicate.Operator);
        Assert.Equal(30, predicate.Value);
        Assert.Null(predicate.ValueTo);
        Assert.Equal("m", predicate.Unit);
    }

    [Fact]
    public void Constructor_WithValueTo_SetsValueTo()
    {
        var predicate = new AttributePredicate(
            Field: "year-built",
            Operator: AttributeOperator.Between,
            Value: 1980,
            ValueTo: 2000);

        Assert.Equal(1980, predicate.Value);
        Assert.Equal(2000, predicate.ValueTo);
        Assert.Null(predicate.Unit);
    }

    [Fact]
    public void WithUnit_ProducesInferredCopy()
    {
        var predicate = new AttributePredicate("area", AttributeOperator.Gt, 5000);

        var inferred = predicate with { Unit = "m2" };

        Assert.Null(predicate.Unit);
        Assert.Equal("m2", inferred.Unit);
        Assert.Equal(predicate.Field, inferred.Field);
    }
}
