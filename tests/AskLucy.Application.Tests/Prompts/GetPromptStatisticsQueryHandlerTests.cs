using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts;
using AskLucy.Application.Prompts.Queries.GetPromptStatistics;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>tasks.md T119. `GetPromptStatisticsQuery` combines `PromptUsageStatistics` (successful-execution count/last-use) with the rating breakdown across every execution of the prompt (spec.md "Prompt Statistics" API requirement, FR-062).</summary>
public sealed class GetPromptStatisticsQueryHandlerTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IPromptExecutionRepository _executionRepository = Substitute.For<IPromptExecutionRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private GetPromptStatisticsQueryHandler CreateHandler() => new(_promptRepository, _executionRepository, _currentUser);

    [Fact]
    public async Task Handle_ShouldCombineUsageStatisticsAndRatingBreakdown()
    {
        _currentUser.UserId.Returns(OwnerId);
        var content = new PromptContentSnapshot(null, null, "x", null, null, null, null, null, null, null, null, false);
        var (prompt, _) = Prompt.Create(
            OwnerId, "Prompt", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null, content, [], OwnerId);
        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);

        var usageStatistics = PromptUsageStatistics.CreateEmpty(prompt.Id, OwnerId);
        usageStatistics.RecordSuccessfulUse();
        usageStatistics.RecordSuccessfulUse();
        _promptRepository.GetUsageStatisticsAsync(prompt.Id, Arg.Any<CancellationToken>()).Returns(usageStatistics);

        var breakdown = new PromptRatingBreakdownDto(Good: 8, NeedsImprovement: 3, Failed: 1);
        _executionRepository.GetRatingBreakdownByPromptIdAsync(prompt.Id, Arg.Any<CancellationToken>()).Returns(breakdown);

        var result = await CreateHandler().Handle(new GetPromptStatisticsQuery(prompt.Id), CancellationToken.None);

        result.SuccessfulExecutionCount.Should().Be(2);
        result.LastSuccessfulUseAtUtc.Should().NotBeNull();
        result.RatingBreakdown.Should().Be(breakdown);
    }

    [Fact]
    public async Task Handle_ShouldReturnZeroCounts_WhenNoUsageStatisticsRowExistsYet()
    {
        _currentUser.UserId.Returns(OwnerId);
        var content = new PromptContentSnapshot(null, null, "x", null, null, null, null, null, null, null, null, false);
        var (prompt, _) = Prompt.Create(
            OwnerId, "Prompt", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null, content, [], OwnerId);
        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetUsageStatisticsAsync(prompt.Id, Arg.Any<CancellationToken>()).Returns((PromptUsageStatistics?)null);
        _executionRepository.GetRatingBreakdownByPromptIdAsync(prompt.Id, Arg.Any<CancellationToken>())
            .Returns(new PromptRatingBreakdownDto(0, 0, 0));

        var result = await CreateHandler().Handle(new GetPromptStatisticsQuery(prompt.Id), CancellationToken.None);

        result.SuccessfulExecutionCount.Should().Be(0);
        result.LastSuccessfulUseAtUtc.Should().BeNull();
    }
}
