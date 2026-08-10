using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts;
using AskLucy.Application.Prompts.Commands.ImportPrompts;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>tasks.md T111. A name collision on an imported entry is auto-suffixed rather than failing the whole import (FR-072) — mirrors <c>DuplicatePromptCommandHandler</c>'s identical algorithm.</summary>
public sealed class PromptImportNameCollisionTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IPromptAuditLogRepository _auditLogRepository = Substitute.For<IPromptAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private ImportPromptsCommandHandler CreateHandler() =>
        new(_promptRepository, _auditLogRepository, _unitOfWork, _currentUser);

    private static PromptExportEntry BuildEntry(string name) => new(
        name, null, PromptType.Chat, null, null, "Summarize {{document}}.", null, null, null, null,
        PromptCapabilityRequirements.None, null,
        [new PromptVariableDto("document", null, PromptVariableType.File, true, null, null, null, 0)], []);

    [Fact]
    public async Task Handle_ShouldAutoSuffixTheName_WhenAnEntryCollidesWithAnExistingPrompt()
    {
        _currentUser.UserId.Returns(OwnerId);
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, "Prompt A", Arg.Any<CancellationToken>()).Returns(Task.FromResult<Prompt?>(
            Prompt.Create(
                OwnerId, "Prompt A", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null,
                new PromptContentSnapshot(null, null, "x", null, null, null, null, null, null, null, null, false), [], OwnerId).Prompt));
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, "Prompt A 2", Arg.Any<CancellationToken>()).Returns((Prompt?)null);

        var file = new PromptExportFile(PromptExportFile.CurrentSchemaVersion, [BuildEntry("Prompt A")]);

        Prompt? created = null;
        _promptRepository.When(r => r.Add(Arg.Any<Prompt>())).Do(c => created = c.Arg<Prompt>());

        var result = await CreateHandler().Handle(new ImportPromptsCommand(file), CancellationToken.None);

        result.Should().ContainSingle();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Prompt A 2");
    }

    [Fact]
    public async Task Handle_ShouldNotFailTheWholeImport_WhenOnlyOneOfSeveralEntriesCollides()
    {
        _currentUser.UserId.Returns(OwnerId);
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, "Prompt A", Arg.Any<CancellationToken>()).Returns(Task.FromResult<Prompt?>(
            Prompt.Create(
                OwnerId, "Prompt A", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null,
                new PromptContentSnapshot(null, null, "x", null, null, null, null, null, null, null, null, false), [], OwnerId).Prompt));
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, "Prompt A 2", Arg.Any<CancellationToken>()).Returns((Prompt?)null);
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, "Prompt B", Arg.Any<CancellationToken>()).Returns((Prompt?)null);

        var file = new PromptExportFile(PromptExportFile.CurrentSchemaVersion, [BuildEntry("Prompt A"), BuildEntry("Prompt B")]);

        var result = await CreateHandler().Handle(new ImportPromptsCommand(file), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(p => p.Name).Should().BeEquivalentTo(["Prompt A 2", "Prompt B"]);
    }
}
