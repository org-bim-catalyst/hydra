using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Commands.ExportPrompts;
using AskLucy.Application.Prompts.Commands.ImportPrompts;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>
/// tasks.md T110. Export → delete → import round-trip recreates content/variables/current version/
/// model settings/tags exactly, as an independent new prompt with its own fresh version-1 history
/// (FR-070–FR-072, SC-007). <c>PromptExportFileBuilder</c>/<c>PromptImportValidator</c> are pure
/// static classes (no reason to fake) exercised for real here, with faked repositories, same style
/// as the rest of this suite.
/// </summary>
public sealed class PromptExportImportRoundTripTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IPromptAuditLogRepository _auditLogRepository = Substitute.For<IPromptAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private ExportPromptsCommandHandler CreateExportHandler() =>
        new(_promptRepository, _auditLogRepository, _unitOfWork, _currentUser);

    private ImportPromptsCommandHandler CreateImportHandler() =>
        new(_promptRepository, _auditLogRepository, _unitOfWork, _currentUser);

    private static (Prompt Prompt, PromptVersion Version) BuildSourcePrompt()
    {
        var content = new PromptContentSnapshot(
            "You are a technical writer.", "Be concise.", "Summarize {{document}} in {{target_language}}.",
            "Internal use only.", "Example: ...", "Return plain text.", "Under 200 words.",
            null, null, null, null, false);
        var variables = new List<PromptVariableDefinition>
        {
            new("document", "Source document", PromptVariableType.File, true, null, null, null, 0),
            new("target_language", "Output language", PromptVariableType.String, false, "English", null, null, 1),
        };
        var (prompt, version) = Prompt.Create(
            OwnerId, "Technical Documentation Generator", "Generates technical docs.", PromptType.Summarization,
            null, null, PromptCapabilityRequirements.None, "gpt-5", content, variables, OwnerId);
        prompt.AddTag("technical-writing", OwnerId, OwnerId);
        prompt.AddTag("documentation", OwnerId, OwnerId);
        return (prompt, version);
    }

    [Fact]
    public async Task ExportThenImport_ShouldRecreateContentVariablesModelSettingsAndTags_AsAnIndependentPromptWithFreshVersionOneHistory()
    {
        _currentUser.UserId.Returns(OwnerId);
        var (source, version) = BuildSourcePrompt();
        _promptRepository.GetByIdForOwnerAsync(source.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(source);
        _promptRepository.GetVersionAsync(source.Id, source.CurrentVersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        // No name collision — the source itself is presumed deleted before re-import (per this
        // test's own "export -> delete -> import" scenario), so no other prompt shares its name.
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, source.Name, Arg.Any<CancellationToken>()).Returns((Prompt?)null);

        var exported = await CreateExportHandler().Handle(new ExportPromptsCommand([source.Id]), CancellationToken.None);

        Prompt? importedPrompt = null;
        _promptRepository.When(r => r.Add(Arg.Any<Prompt>())).Do(c => importedPrompt = c.Arg<Prompt>());

        var created = await CreateImportHandler().Handle(new ImportPromptsCommand(exported), CancellationToken.None);

        created.Should().ContainSingle();
        importedPrompt.Should().NotBeNull();
        importedPrompt!.Id.Should().NotBe(source.Id);
        importedPrompt.Name.Should().Be(source.Name);
        importedPrompt.Description.Should().Be(source.Description);
        importedPrompt.PromptType.Should().Be(source.PromptType);
        importedPrompt.PreferredModelKey.Should().Be(source.PreferredModelKey);
        importedPrompt.SystemInstructions.Should().Be(version.SystemInstructions);
        importedPrompt.UserInstructions.Should().Be(version.UserInstructions);
        importedPrompt.Tags.Select(t => t.Value).Should().BeEquivalentTo(source.Tags.Select(t => t.Value));

        // Fresh version-1 history — never re-uses the source's version count/history.
        importedPrompt.CurrentVersionNumber.Should().Be(1);
        importedPrompt.Versions.Should().ContainSingle();
        var importedVariables = importedPrompt.Versions.Single().Variables.OrderBy(v => v.OrderIndex).ToList();
        importedVariables.Select(v => v.Name).Should().Equal(version.Variables.OrderBy(v => v.OrderIndex).Select(v => v.Name));
        importedVariables.Select(v => v.IsRequired).Should().Equal(version.Variables.OrderBy(v => v.OrderIndex).Select(v => v.IsRequired));
    }
}
