using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Commands.RecordPromptExecution;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>
/// tasks.md T082. The `view=recentlyUsed` ordering itself is a real SQL query
/// (<c>PromptRepository.SearchAsync</c>'s <c>FetchByRecentlyUsedAsync</c>, covered by
/// <c>PromptSearchTests</c> against a live database) driven entirely by
/// <see cref="PromptUsageStatistics.LastSuccessfulUseAtUtc"/>. The actual "only a successful
/// execution moves a prompt up" rule (spec.md Clarifications 2026-08-10, FR-051) is enforced here,
/// in <see cref="RecordPromptExecutionCommandHandler"/> — it is the only place
/// <see cref="PromptUsageStatistics.RecordSuccessfulUse"/> is ever called, and only when the
/// execution outcome is <see cref="PromptExecutionOutcome.Success"/>.
/// </summary>
public sealed class RecentlyUsedOrderingTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IPromptExecutionRepository _executionRepository = Substitute.For<IPromptExecutionRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private RecordPromptExecutionCommandHandler CreateHandler() =>
        new(_promptRepository, _executionRepository, _modelRepository, _unitOfWork, _currentUser);

    private static RecordPromptExecutionCommand BuildCommand(Guid promptId, PromptExecutionOutcome outcome) => new(
        promptId, Guid.NewGuid(), PromptExecutionOrigin.TestingWorkspace, Guid.NewGuid(), "openai", "gpt-5",
        Temperature: null, MaxOutputTokens: null, StructuredOutputRequested: false, ResolvedVariableValuesJson: "{}",
        RequestedRagContext: false, RequestedMemoryContext: false, Outcome: outcome,
        ErrorDetail: outcome == PromptExecutionOutcome.Failed ? "Provider timed out." : null,
        LatencyMs: 120, OutputText: "result", InputTokenCount: 10, OutputTokenCount: 5,
        RagCitationsJson: null, MemoryReferencesJson: null);

    [Fact]
    public async Task Handle_ShouldRecordSuccessfulUse_WhenOutcomeIsSuccess()
    {
        _currentUser.UserId.Returns(OwnerId);
        var promptId = Guid.NewGuid();
        var usageStatistics = PromptUsageStatistics.CreateEmpty(promptId, OwnerId);
        _promptRepository.GetUsageStatisticsAsync(promptId, Arg.Any<CancellationToken>()).Returns(usageStatistics);

        await CreateHandler().Handle(BuildCommand(promptId, PromptExecutionOutcome.Success), CancellationToken.None);

        usageStatistics.SuccessfulExecutionCount.Should().Be(1);
        usageStatistics.LastSuccessfulUseAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ShouldNotRecordUse_WhenOutcomeIsFailed()
    {
        // A failed execution must never move a prompt up the recently-used view — asserting the
        // usage row is untouched is what proves the ordering itself is correct upstream.
        _currentUser.UserId.Returns(OwnerId);
        var promptId = Guid.NewGuid();
        var usageStatistics = PromptUsageStatistics.CreateEmpty(promptId, OwnerId);
        _promptRepository.GetUsageStatisticsAsync(promptId, Arg.Any<CancellationToken>()).Returns(usageStatistics);

        await CreateHandler().Handle(BuildCommand(promptId, PromptExecutionOutcome.Failed), CancellationToken.None);

        usageStatistics.SuccessfulExecutionCount.Should().Be(0);
        usageStatistics.LastSuccessfulUseAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RepeatedSuccesses_ShouldKeepAdvancingLastSuccessfulUseAtUtc()
    {
        _currentUser.UserId.Returns(OwnerId);
        var promptId = Guid.NewGuid();
        var usageStatistics = PromptUsageStatistics.CreateEmpty(promptId, OwnerId);
        _promptRepository.GetUsageStatisticsAsync(promptId, Arg.Any<CancellationToken>()).Returns(usageStatistics);

        await CreateHandler().Handle(BuildCommand(promptId, PromptExecutionOutcome.Success), CancellationToken.None);
        var firstUseAt = usageStatistics.LastSuccessfulUseAtUtc;
        await Task.Delay(10);
        await CreateHandler().Handle(BuildCommand(promptId, PromptExecutionOutcome.Success), CancellationToken.None);

        usageStatistics.SuccessfulExecutionCount.Should().Be(2);
        usageStatistics.LastSuccessfulUseAtUtc.Should().BeOnOrAfter(firstUseAt!.Value);
    }
}
