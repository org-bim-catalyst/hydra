using AskLucy.Application.Prompts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

public sealed class PromptContentAnalyzerTests
{
    [Fact]
    public void DetectPlaceholders_ShouldFindEveryDistinctName()
    {
        var placeholders = PromptContentAnalyzer.DetectPlaceholders("Summarize {{document}} in {{ target_language }} at {{document}} length.");

        placeholders.Should().BeEquivalentTo(["document", "target_language"]);
    }

    [Fact]
    public void DetectPlaceholders_ShouldReturnEmpty_WhenContentIsNullOrHasNoPlaceholders()
    {
        PromptContentAnalyzer.DetectPlaceholders(null).Should().BeEmpty();
        PromptContentAnalyzer.DetectPlaceholders("No variables here.").Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ShouldBeValid_WhenEveryPlaceholderHasAMatchingDeclaredVariable()
    {
        var result = PromptContentAnalyzer.Analyze(
            ["Summarize {{document}} in {{target_language}}."], ["document", "target_language"]);

        result.IsValid.Should().BeTrue();
        result.UndeclaredPlaceholders.Should().BeEmpty();
        result.UnreferencedVariables.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ShouldFlagUndeclaredPlaceholders()
    {
        var result = PromptContentAnalyzer.Analyze(["Summarize {{document}}."], []);

        result.IsValid.Should().BeFalse();
        result.UndeclaredPlaceholders.Should().ContainSingle().Which.Should().Be("document");
    }

    [Fact]
    public void Analyze_ShouldFlagUnreferencedVariables()
    {
        var result = PromptContentAnalyzer.Analyze(["No placeholders here."], ["document"]);

        result.IsValid.Should().BeFalse();
        result.UnreferencedVariables.Should().ContainSingle().Which.Should().Be("document");
    }

    [Fact]
    public void Analyze_ShouldScanEveryContentField_NotJustTheFirst()
    {
        var result = PromptContentAnalyzer.Analyze(
            ["System has {{a}}.", null, "User has {{b}}."], ["a", "b"]);

        result.IsValid.Should().BeTrue();
        result.ReferencedPlaceholders.Should().BeEquivalentTo(["a", "b"]);
    }
}
