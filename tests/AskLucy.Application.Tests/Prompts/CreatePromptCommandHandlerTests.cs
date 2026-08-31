using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts;
using AskLucy.Application.Prompts.Commands.CreatePrompt;
using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

public sealed class CreatePromptCommandHandlerTests
{
    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IPromptAuditLogRepository _auditLogRepository = Substitute.For<IPromptAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private CreatePromptCommandHandler CreateHandler() => new(_promptRepository, _auditLogRepository, _unitOfWork, _currentUser);

    private static CreatePromptCommand ValidCommand(string name = "Summarize a document") => new(
        name, "desc", PromptType.Summarization,
        "You are a summarizer.", null, "Summarize {{document}} in {{target_language}}.",
        null, null, null, null, null, null,
        PromptCapabilityRequirements.None, null,
        [new PromptVariableDto("document", null, PromptVariableType.File, true, null, null, null, 0),
         new PromptVariableDto("target_language", null, PromptVariableType.String, false, "English", null, null, 1)]);

    [Fact]
    public async Task Handle_ShouldCreateAPromptOwnedByTheCaller()
    {
        _currentUser.UserId.Returns("user-1");
        _promptRepository.GetByOwnerAndNameAsync("user-1", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Prompt?)null);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.Name.Should().Be("Summarize a document");
        result.Variables.Should().HaveCount(2);
        result.CurrentVersion.VersionNumber.Should().Be(1);
        _promptRepository.Received(1).Add(Arg.Is<Prompt>(p => p != null && p.OwnerId == "user-1"));
        _promptRepository.Received(1).AddUsageStatistics(Arg.Any<PromptUsageStatistics>());
        _auditLogRepository.Received(1).Add(Arg.Is<PromptAuditLog>(a => a != null && a.Action == PromptAuditAction.Created));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRejectADuplicateName_CaseInsensitive_ForTheSameOwner()
    {
        _currentUser.UserId.Returns("user-1");
        var (existing, _) = Prompt.Create(
            "user-1", "summarize a document", null, PromptType.Chat, null, null,
            PromptCapabilityRequirements.None, null,
            new PromptContentSnapshot(null, null, "hi", null, null, null, null, null, null, null, null, false), [], "user-1");
        _promptRepository.GetByOwnerAndNameAsync("user-1", "Summarize a document", Arg.Any<CancellationToken>()).Returns(existing);

        var act = () => CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateResourceException>();
        _promptRepository.DidNotReceive().Add(Arg.Any<Prompt>());
    }

    [Fact]
    public async Task Handle_ShouldReject_WhenContentReferencesAnUndeclaredVariable()
    {
        _currentUser.UserId.Returns("user-1");
        _promptRepository.GetByOwnerAndNameAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Prompt?)null);

        var command = ValidCommand() with { Variables = [] };

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        _promptRepository.DidNotReceive().Add(Arg.Any<Prompt>());
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
