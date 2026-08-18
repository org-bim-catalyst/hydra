using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Prompts;

public sealed class PromptTests
{
    private const string OwnerId = "user-1";

    private static PromptContentSnapshot Content(string userInstructions = "Summarize {{document}}.") => new(
        SystemInstructions: "You are a summarizer.",
        DeveloperInstructions: null,
        UserInstructions: userInstructions,
        ContextText: null,
        ExamplesText: null,
        OutputInstructions: null,
        Constraints: null,
        ProviderKey: null,
        ModelKey: null,
        Temperature: null,
        MaxOutputTokens: null,
        StructuredOutputRequested: false);

    private static readonly List<PromptVariableDefinition> Variables =
    [
        new("document", "Source document", PromptVariableType.File, IsRequired: true, null, null, null, 0),
    ];

    [Fact]
    public void Create_ShouldProduceVersionOne_AndSyncContentOntoPrompt()
    {
        var (prompt, version) = Prompt.Create(
            OwnerId, "Summarize a document", "desc", PromptType.Summarization, null, null,
            PromptCapabilityRequirements.None, null, Content(), Variables, OwnerId);

        version.VersionNumber.Should().Be(1);
        prompt.CurrentVersionId.Should().Be(version.Id);
        prompt.CurrentVersionNumber.Should().Be(1);
        prompt.UserInstructions.Should().Be("Summarize {{document}}.");
        prompt.Versions.Should().ContainSingle().Which.Should().BeSameAs(version);
    }

    [Fact]
    public void Create_ShouldThrow_WhenNameIsBlank()
    {
        var act = () => Prompt.Create(
            OwnerId, "  ", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null,
            Content(), Variables, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void ApplyEdit_ShouldCreateANewVersion_AndAdvanceCurrentVersion()
    {
        var (prompt, _) = Prompt.Create(
            OwnerId, "Summarize a document", null, PromptType.Summarization, null, null,
            PromptCapabilityRequirements.None, null, Content(), Variables, OwnerId);

        var version2 = prompt.ApplyEdit(Content("Summarize {{document}} in {{target_language}}."),
            [.. Variables, new PromptVariableDefinition("target_language", null, PromptVariableType.String, false, "English", null, null, 1)],
            "Added language variable", OwnerId);

        version2.VersionNumber.Should().Be(2);
        prompt.CurrentVersionId.Should().Be(version2.Id);
        prompt.CurrentVersionNumber.Should().Be(2);
        prompt.Versions.Should().HaveCount(2);
        prompt.UserInstructions.Should().Be("Summarize {{document}} in {{target_language}}.");
    }

    [Fact]
    public void ApplyEdit_TwiceThenNothingElse_ShouldNeverModifyVersionOnesContent()
    {
        // Guards FR-020 ("execute repeatedly without modifying the template") indirectly:
        // a Prompt is never versioned by anything other than an explicit ApplyEdit call — this
        // confirms version 1's content is immutable once version 2 exists, so a read-only
        // resolution/execution path (which never calls ApplyEdit) cannot have touched it either.
        var (prompt, version1) = Prompt.Create(
            OwnerId, "Summarize a document", null, PromptType.Summarization, null, null,
            PromptCapabilityRequirements.None, null, Content(), Variables, OwnerId);
        var originalInstructions = version1.UserInstructions;

        prompt.ApplyEdit(Content("Different content entirely."), Variables, null, OwnerId);

        version1.UserInstructions.Should().Be(originalInstructions);
        prompt.Versions.Should().HaveCount(2);
    }

    [Fact]
    public void RestoreFrom_ShouldCreateANewVersionCopyingTheRestoredContent_NeverDeletingHistory()
    {
        var (prompt, version1) = Prompt.Create(
            OwnerId, "Summarize a document", null, PromptType.Summarization, null, null,
            PromptCapabilityRequirements.None, null, Content("Original content {{document}}"), Variables, OwnerId);
        prompt.ApplyEdit(Content("Changed content {{document}}"), Variables, null, OwnerId);

        var restoredVersion = prompt.RestoreFrom(version1, OwnerId);

        restoredVersion.VersionNumber.Should().Be(3);
        restoredVersion.UserInstructions.Should().Be("Original content {{document}}");
        prompt.CurrentVersionId.Should().Be(restoredVersion.Id);
        prompt.Versions.Should().HaveCount(3);
    }

    [Fact]
    public void RestoreFrom_ShouldThrow_WhenVersionBelongsToAnotherPrompt()
    {
        var (_, version1) = Prompt.Create(
            OwnerId, "Prompt A", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null,
            Content(), Variables, OwnerId);
        var (promptB, _) = Prompt.Create(
            OwnerId, "Prompt B", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null,
            Content(), Variables, OwnerId);

        var act = () => promptB.RestoreFrom(version1, OwnerId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Rename_SetFolder_SetFavorite_ShouldNotCreateNewVersions()
    {
        var (prompt, _) = Prompt.Create(
            OwnerId, "Summarize a document", null, PromptType.Summarization, null, null,
            PromptCapabilityRequirements.None, null, Content(), Variables, OwnerId);

        prompt.Rename("Renamed prompt", OwnerId);
        prompt.SetFolder(Guid.NewGuid(), OwnerId);
        prompt.SetFavorite(true, OwnerId);

        prompt.Versions.Should().ContainSingle();
        prompt.Name.Should().Be("Renamed prompt");
        prompt.IsFavorite.Should().BeTrue();
    }

    [Fact]
    public void SoftDelete_ShouldSetDeletedAudit()
    {
        var (prompt, _) = Prompt.Create(
            OwnerId, "Summarize a document", null, PromptType.Summarization, null, null,
            PromptCapabilityRequirements.None, null, Content(), Variables, OwnerId);

        prompt.SoftDelete(OwnerId);

        prompt.IsDeleted.Should().BeTrue();
        prompt.DeletedBy.Should().Be(OwnerId);
    }
}
