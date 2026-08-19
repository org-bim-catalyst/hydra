using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

public sealed class WorkflowExpressionEvaluatorTests
{
    private readonly WorkflowExpressionEvaluator _evaluator = new();

    [Theory]
    [InlineData("\"hello\"", WorkflowExpressionValueKind.String)]
    [InlineData("42", WorkflowExpressionValueKind.Number)]
    [InlineData("true", WorkflowExpressionValueKind.Boolean)]
    [InlineData("false", WorkflowExpressionValueKind.Boolean)]
    [InlineData("null", WorkflowExpressionValueKind.Null)]
    public void Evaluate_ShouldResolveLiterals(string expression, WorkflowExpressionValueKind expectedKind)
    {
        var ast = _evaluator.Parse(expression);

        var result = _evaluator.Evaluate(ast, new Dictionary<string, WorkflowExpressionValue>());

        result.Kind.Should().Be(expectedKind);
    }

    [Fact]
    public void Evaluate_ShouldResolveReferences()
    {
        var ast = _evaluator.Parse("{{workflow.threshold}}");
        var values = new Dictionary<string, WorkflowExpressionValue> { ["workflow.threshold"] = WorkflowExpressionValue.OfNumber(10) };

        var result = _evaluator.Evaluate(ast, values);

        result.NumberValue.Should().Be(10);
    }

    [Fact]
    public void Evaluate_ShouldThrow_WhenReferenceIsUnresolved()
    {
        var ast = _evaluator.Parse("{{workflow.missing}}");

        var act = () => _evaluator.Evaluate(ast, new Dictionary<string, WorkflowExpressionValue>());

        act.Should().Throw<WorkflowExpressionEvaluationException>();
    }

    [Theory]
    [InlineData("5 > 3", true)]
    [InlineData("5 < 3", false)]
    [InlineData("5 >= 5", true)]
    [InlineData("5 <= 4", false)]
    [InlineData("\"a\" == \"a\"", true)]
    [InlineData("\"a\" != \"b\"", true)]
    public void Evaluate_ShouldResolveComparisons(string expression, bool expected)
    {
        var ast = _evaluator.Parse(expression);

        var result = _evaluator.Evaluate(ast, new Dictionary<string, WorkflowExpressionValue>());

        result.BooleanValue.Should().Be(expected);
    }

    [Theory]
    [InlineData("true AND false", false)]
    [InlineData("true OR false", true)]
    [InlineData("NOT true", false)]
    [InlineData("NOT false AND true", true)]
    [InlineData("(true OR false) AND NOT false", true)]
    public void Evaluate_ShouldResolveNestedLogicalExpressions(string expression, bool expected)
    {
        var ast = _evaluator.Parse(expression);

        var result = _evaluator.Evaluate(ast, new Dictionary<string, WorkflowExpressionValue>());

        result.BooleanValue.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_Concat_ShouldJoinArguments()
    {
        var ast = _evaluator.Parse("concat(\"a\", \"b\", \"c\")");

        var result = _evaluator.Evaluate(ast, new Dictionary<string, WorkflowExpressionValue>());

        result.StringValue.Should().Be("abc");
    }

    [Fact]
    public void Evaluate_Length_ShouldReturnStringLength()
    {
        var ast = _evaluator.Parse("length(\"hello\")");

        var result = _evaluator.Evaluate(ast, new Dictionary<string, WorkflowExpressionValue>());

        result.NumberValue.Should().Be(5);
    }

    [Fact]
    public void Evaluate_Contains_ShouldCheckSubstring()
    {
        var ast = _evaluator.Parse("contains(\"hello world\", \"world\")");

        var result = _evaluator.Evaluate(ast, new Dictionary<string, WorkflowExpressionValue>());

        result.BooleanValue.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_IsEmpty_ShouldDetectEmptyString()
    {
        var ast = _evaluator.Parse("isEmpty(\"\")");

        var result = _evaluator.Evaluate(ast, new Dictionary<string, WorkflowExpressionValue>());

        result.BooleanValue.Should().BeTrue();
    }

    [Fact]
    public void ValidateTypes_ShouldFlagUnknownReference()
    {
        var ast = _evaluator.Parse("{{steps.missing.value}} == \"x\"");

        var errors = _evaluator.ValidateTypes(ast, new Dictionary<string, WorkflowVariableType>());

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateTypes_ShouldFlagTypeMismatchOnOrderingOperator()
    {
        var ast = _evaluator.Parse("{{workflow.name}} > 5");
        var knownTypes = new Dictionary<string, WorkflowVariableType> { ["workflow.name"] = WorkflowVariableType.String };

        var errors = _evaluator.ValidateTypes(ast, knownTypes);

        errors.Should().ContainSingle();
    }

    [Fact]
    public void ValidateTypes_ShouldPass_ForAWellTypedCondition()
    {
        var ast = _evaluator.Parse("{{workflow.score}} >= 0.8");
        var knownTypes = new Dictionary<string, WorkflowVariableType> { ["workflow.score"] = WorkflowVariableType.Number };

        var errors = _evaluator.ValidateTypes(ast, knownTypes);

        errors.Should().BeEmpty();
    }
}
