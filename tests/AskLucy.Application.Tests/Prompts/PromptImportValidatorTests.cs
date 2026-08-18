using AskLucy.Application.Prompts;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>
/// tasks.md T109. Valid single/bundle files pass; a missing required field, unknown schema version,
/// or malformed variable rejects the entire file (FR-071, research.md Decision 13). Relocated from
/// the originally-planned `AskLucy.Infrastructure.Tests` to `AskLucy.Application.Tests`:
/// <c>PromptImportValidator</c> turned out to have zero external (file-system/network) dependency —
/// a plain static class mirroring <c>PromptContentAnalyzer</c>/<c>PromptVariableResolver</c>'s
/// identical "no DI indirection needed" convention, living in <c>AskLucy.Application.Prompts</c>
/// rather than <c>AskLucy.Infrastructure</c> as tasks.md originally assumed (avoids
/// <c>AskLucy.Application.Tests</c> needing a project reference to Infrastructure it otherwise
/// never requires — constitution §3 Dependency Rule applies to test projects' own layering too).
/// </summary>
public sealed class PromptImportValidatorTests
{
    private static PromptExportEntry ValidEntry(string name = "Summarize a document") => new(
        name, "Summarizes a document.", PromptType.Summarization, "You are a summarizer.", null,
        "Summarize {{document}}.", null, null, null, null,
        PromptCapabilityRequirements.None, null,
        [new PromptVariableDto("document", null, PromptVariableType.File, true, null, null, null, 0)],
        ["technical-writing"]);

    [Fact]
    public void Validate_ShouldPass_ForAValidSinglePromptFile()
    {
        var file = new PromptExportFile(PromptExportFile.CurrentSchemaVersion, [ValidEntry()]);

        var result = PromptImportValidator.Validate(file);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldPass_ForAValidBundleFile()
    {
        var file = new PromptExportFile(
            PromptExportFile.CurrentSchemaVersion, [ValidEntry("Prompt A"), ValidEntry("Prompt B"), ValidEntry("Prompt C")]);

        var result = PromptImportValidator.Validate(file);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldReject_WhenSchemaVersionIsUnrecognized()
    {
        var file = new PromptExportFile(999, [ValidEntry()]);

        var result = PromptImportValidator.Validate(file);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.EntryIndex == -1 && e.Message.Contains("schema version"));
    }

    [Fact]
    public void Validate_ShouldReject_WhenAnEntryIsMissingARequiredField()
    {
        var missingName = ValidEntry() with { Name = "   " };
        var file = new PromptExportFile(PromptExportFile.CurrentSchemaVersion, [missingName]);

        var result = PromptImportValidator.Validate(file);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.EntryIndex == 0 && e.Message.Contains("name"));
    }

    [Fact]
    public void Validate_ShouldReject_WhenAVariableIsMalformed_MissingAName()
    {
        var malformedVariable = ValidEntry() with
        {
            Variables = [new PromptVariableDto("", null, PromptVariableType.File, true, null, null, null, 0)],
        };
        var file = new PromptExportFile(PromptExportFile.CurrentSchemaVersion, [malformedVariable]);

        var result = PromptImportValidator.Validate(file);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.EntryIndex == 0 && e.Message.Contains("variable"));
    }

    [Fact]
    public void Validate_ShouldReject_WhenContentReferencesAnUndeclaredPlaceholder()
    {
        var undeclaredPlaceholder = ValidEntry() with { UserInstructions = "Summarize {{document}} in {{target_language}}." };
        var file = new PromptExportFile(PromptExportFile.CurrentSchemaVersion, [undeclaredPlaceholder]);

        var result = PromptImportValidator.Validate(file);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.EntryIndex == 0 && e.Message.Contains("target_language"));
    }

    [Fact]
    public void Validate_ShouldRejectTheWholeFile_WhenOnlyOneEntryAmongSeveralIsInvalid()
    {
        var file = new PromptExportFile(
            PromptExportFile.CurrentSchemaVersion,
            [ValidEntry("Prompt A"), ValidEntry("  ") /* invalid */, ValidEntry("Prompt C")]);

        var result = PromptImportValidator.Validate(file);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.EntryIndex == 1);
    }
}
