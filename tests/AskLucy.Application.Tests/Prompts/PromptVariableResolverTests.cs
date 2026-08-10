using AskLucy.Application.Prompts;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

public sealed class PromptVariableResolverTests
{
    private static readonly PromptContentSnapshot Content = new(
        null, null, "Use {{document}} {{tone}} {{a}} {{b}} {{c}}", null, null, null, null, null, null, null, null, false);

    /// <summary>Builds real <see cref="PromptVariable"/> instances via the public <see cref="Prompt.Create"/>
    /// path — <c>PromptVariable.Create</c> itself is `internal` (only <c>PromptVersion</c> may construct one).</summary>
    private static IReadOnlyCollection<PromptVariable> BuildVariables(params PromptVariableDefinition[] definitions)
    {
        var (_, version) = Prompt.Create(
            "user-1", $"Test prompt {Guid.NewGuid():N}", null, PromptType.Chat, null, null,
            PromptCapabilityRequirements.None, null, Content, definitions, "user-1");
        return version.Variables;
    }

    private static PromptVariableDefinition RequiredStringVariable(string name = "document", int orderIndex = 0) =>
        new(name, null, PromptVariableType.String, true, null, null, null, orderIndex);

    [Fact]
    public void ValidateAndResolve_ShouldFail_WhenARequiredVariableIsMissing()
    {
        var variables = BuildVariables(RequiredStringVariable());

        var result = PromptVariableResolver.ValidateAndResolve(variables, new Dictionary<string, string?>());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.VariableName == "document");
    }

    [Fact]
    public void ValidateAndResolve_ShouldSucceed_WhenARequiredVariableIsSupplied()
    {
        var variables = BuildVariables(RequiredStringVariable());

        var result = PromptVariableResolver.ValidateAndResolve(variables, new Dictionary<string, string?> { ["document"] = "Some text" });

        result.IsValid.Should().BeTrue();
        result.ResolvedValues["document"].Should().Be("Some text");
    }

    [Fact]
    public void ValidateAndResolve_ShouldFallBackToDefaultValue_WhenNotRequiredAndNotSupplied()
    {
        var variables = BuildVariables(new PromptVariableDefinition("tone", null, PromptVariableType.String, false, "neutral", null, null, 0));

        var result = PromptVariableResolver.ValidateAndResolve(variables, new Dictionary<string, string?>());

        result.IsValid.Should().BeTrue();
        result.ResolvedValues["tone"].Should().Be("neutral");
    }

    [Theory]
    [InlineData(PromptVariableType.Number, "not-a-number", false)]
    [InlineData(PromptVariableType.Number, "42", true)]
    [InlineData(PromptVariableType.Boolean, "maybe", false)]
    [InlineData(PromptVariableType.Boolean, "true", true)]
    [InlineData(PromptVariableType.Date, "not-a-date", false)]
    [InlineData(PromptVariableType.Json, "{not json", false)]
    [InlineData(PromptVariableType.Json, "{\"a\":1}", true)]
    public void ValidateAndResolve_ShouldEnforceTypeChecking(PromptVariableType type, string value, bool expectedValid)
    {
        var variables = BuildVariables(new PromptVariableDefinition("a", null, type, true, null, null, null, 0));

        var result = PromptVariableResolver.ValidateAndResolve(variables, new Dictionary<string, string?> { ["a"] = value });

        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void ValidateAndResolve_ShouldEnforceAllowedValues()
    {
        var variables = BuildVariables(new PromptVariableDefinition(
            "tone", null, PromptVariableType.String, true, null, null, "{\"AllowedValues\":[\"formal\",\"casual\"]}", 0));

        var invalid = PromptVariableResolver.ValidateAndResolve(variables, new Dictionary<string, string?> { ["tone"] = "silly" });
        var valid = PromptVariableResolver.ValidateAndResolve(variables, new Dictionary<string, string?> { ["tone"] = "formal" });

        invalid.IsValid.Should().BeFalse();
        valid.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ResolveContent_ShouldSubstituteEveryPlaceholder()
    {
        var resolved = PromptVariableResolver.ResolveContent(
            "Summarize {{document}} in {{ target_language }}.",
            new Dictionary<string, string> { ["document"] = "the report", ["target_language"] = "French" });

        resolved.Should().Be("Summarize the report in French.");
    }

    [Fact]
    public void ResolveForPreview_ShouldNeverBlock_FallingBackThroughDefaultThenExampleThenPlaceholder()
    {
        var variables = BuildVariables(
            new PromptVariableDefinition("a", null, PromptVariableType.String, true, "default-a", null, null, 0),
            new PromptVariableDefinition("b", null, PromptVariableType.String, true, null, "example-b", null, 1),
            new PromptVariableDefinition("c", null, PromptVariableType.String, true, null, null, null, 2));

        var resolved = PromptVariableResolver.ResolveForPreview(variables, new Dictionary<string, string?>());

        resolved["a"].Should().Be("default-a");
        resolved["b"].Should().Be("example-b");
        resolved["c"].Should().Be("[c]");
    }
}
