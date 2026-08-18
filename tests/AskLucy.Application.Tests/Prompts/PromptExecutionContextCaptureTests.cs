using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Commands.RecordPromptExecution;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>
/// tasks.md T104. <see cref="RecordPromptExecutionCommandHandler"/> threads
/// <see cref="RecordPromptExecutionCommand.RagCitationsJson"/>/<see cref="RecordPromptExecutionCommand.MemoryReferencesJson"/>
/// straight onto the persisted <see cref="PromptExecutionResult"/> (FR-081/FR-082) — populated
/// when the caller supplied them (a grounded/found outcome), left null otherwise. Only
/// <see cref="PromptExecutionOrigin.TestingWorkspace"/> creates a <see cref="PromptExecutionResult"/>
/// at all, matching that entity's own documented scope.
/// </summary>
public sealed class PromptExecutionContextCaptureTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IPromptExecutionRepository _executionRepository = Substitute.For<IPromptExecutionRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private RecordPromptExecutionCommandHandler CreateHandler() =>
        new(_promptRepository, _executionRepository, _modelRepository, _unitOfWork, _currentUser);

    private static RecordPromptExecutionCommand BuildCommand(string? ragCitationsJson, string? memoryReferencesJson) => new(
        Guid.NewGuid(), Guid.NewGuid(), PromptExecutionOrigin.TestingWorkspace, Guid.NewGuid(), "openai", "gpt-5",
        Temperature: null, MaxOutputTokens: null, StructuredOutputRequested: false, ResolvedVariableValuesJson: "{}",
        RequestedRagContext: ragCitationsJson is not null, RequestedMemoryContext: memoryReferencesJson is not null,
        Outcome: PromptExecutionOutcome.Success, ErrorDetail: null, LatencyMs: 120, OutputText: "result",
        InputTokenCount: 10, OutputTokenCount: 5, RagCitationsJson: ragCitationsJson, MemoryReferencesJson: memoryReferencesJson);

    [Fact]
    public async Task Handle_ShouldPersistRagCitationsAndMemoryReferences_WhenBothAreSupplied()
    {
        _currentUser.UserId.Returns(OwnerId);
        var command = BuildCommand("[{\"documentTitle\":\"Q3 Report\"}]", "[{\"memoryId\":\"11111111-1111-1111-1111-111111111111\"}]");

        PromptExecutionResult? captured = null;
        _executionRepository.When(r => r.AddResult(Arg.Any<PromptExecutionResult>())).Do(c => captured = c.Arg<PromptExecutionResult>());

        await CreateHandler().Handle(command, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.RagCitationsJson.Should().Be(command.RagCitationsJson);
        captured.MemoryReferencesJson.Should().Be(command.MemoryReferencesJson);
    }

    [Fact]
    public async Task Handle_ShouldLeaveRagCitationsAndMemoryReferencesNull_WhenNeitherWasRequested()
    {
        _currentUser.UserId.Returns(OwnerId);
        var command = BuildCommand(null, null);

        PromptExecutionResult? captured = null;
        _executionRepository.When(r => r.AddResult(Arg.Any<PromptExecutionResult>())).Do(c => captured = c.Arg<PromptExecutionResult>());

        await CreateHandler().Handle(command, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.RagCitationsJson.Should().BeNull();
        captured.MemoryReferencesJson.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldNotCreateAnExecutionResult_ForConversationInsertionOrigin()
    {
        // PromptExecutionResult is TestingWorkspace-only (data-model.md) — a ConversationInsertion
        // execution's output already lives on the referenced Chats.Message.
        _currentUser.UserId.Returns(OwnerId);
        var command = BuildCommand("[{\"documentTitle\":\"Q3 Report\"}]", null) with { Origin = PromptExecutionOrigin.ConversationInsertion };

        await CreateHandler().Handle(command, CancellationToken.None);

        _executionRepository.DidNotReceive().AddResult(Arg.Any<PromptExecutionResult>());
    }
}
