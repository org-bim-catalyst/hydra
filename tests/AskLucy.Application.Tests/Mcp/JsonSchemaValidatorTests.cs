using System.Text.Json;
using AskLucy.Application.Mcp.Validation;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class JsonSchemaValidatorTests
{
    private readonly JsonSchemaValidator _validator = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Validate_ShouldReturnNoErrors_ForValidInput()
    {
        var schema = Parse("""{"type":"object","required":["name"],"properties":{"name":{"type":"string"}}}""");
        var instance = Parse("""{"name":"search"}""");

        var errors = _validator.Validate(schema, instance, maxSizeBytes: 1_000_000);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldReturnErrors_ForMissingRequiredField()
    {
        var schema = Parse("""{"type":"object","required":["name"],"properties":{"name":{"type":"string"}}}""");
        var instance = Parse("""{}""");

        var errors = _validator.Validate(schema, instance, maxSizeBytes: 1_000_000);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_ShouldReturnErrors_ForWrongType()
    {
        var schema = Parse("""{"type":"object","properties":{"count":{"type":"integer"}}}""");
        var instance = Parse("""{"count":"not-a-number"}""");

        var errors = _validator.Validate(schema, instance, maxSizeBytes: 1_000_000);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_ShouldRejectOversizedPayload_EvenWhenSchemaValid()
    {
        var schema = Parse("""{"type":"object"}""");
        var instance = Parse($$"""{"data":"{{new string('x', 1000)}}"}""");

        var errors = _validator.Validate(schema, instance, maxSizeBytes: 100);

        errors.Should().ContainSingle(e => e.Contains("exceeds the maximum", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldReturnErrors_ForMalformedSchemaDocument()
    {
        var schema = Parse("""{"type":"not-a-real-type-keyword-value", "properties": "should-be-an-object-not-a-string"}""");
        var instance = Parse("""{"name":"search"}""");

        var errors = _validator.Validate(schema, instance, maxSizeBytes: 1_000_000);

        errors.Should().NotBeEmpty();
    }
}
