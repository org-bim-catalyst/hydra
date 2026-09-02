using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Memory.Commands.CreateMemoryCandidate;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Memory;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class MemoryWriteToolIntegrationTests
{
    private static AgentToolExecutionContext Context(string userId = "user-1") =>
        new(Guid.NewGuid(), Guid.NewGuid(), userId, Guid.NewGuid(), Guid.NewGuid(), UserChatId: null);

    [Fact]
    public async Task MemoryWriteTool_ShouldSendCreateMemoryCandidateCommand_WithTheContextsUserId()
    {
        var sender = Substitute.For<ISender>();
        var candidateId = Guid.NewGuid();
        sender.Send(Arg.Any<CreateMemoryCandidateCommand>(), Arg.Any<CancellationToken>()).Returns(candidateId);

        var tool = new MemoryWriteTool(sender);
        using var input = JsonDocument.Parse("""{"content":"Prefers dark mode.","category":"UserPreference"}""");

        var result = await tool.ExecuteAsync(Context("user-42"), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("memoryId").GetGuid().Should().Be(candidateId);
        await sender.Received(1).Send(Arg.Is<CreateMemoryCandidateCommand>(c => c != null && c.UserId == "user-42" && c.Content == "Prefers dark mode." && c.Category == MemoryCategory.UserPreference), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MemoryWriteTool_ShouldFail_WhenContentIsMissing()
    {
        var sender = Substitute.For<ISender>();
        var tool = new MemoryWriteTool(sender);
        using var input = JsonDocument.Parse("""{"category":"UserPreference"}""");

        var result = await tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        await sender.DidNotReceive().Send(Arg.Any<CreateMemoryCandidateCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateMemoryCandidateCommandHandler_ShouldCreateAPendingCandidate_ReusingTheExistingApprovalLifecycle()
    {
        var memoryRepository = Substitute.For<IMemoryRepository>();
        var preferenceRepository = Substitute.For<IMemoryPreferenceRepository>();
        var approvalRepository = Substitute.For<IMemoryApprovalRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var categoryPreference = MemoryCategoryPreference.CreateDefault("user-1", MemoryCategory.UserPreference, "system");
        preferenceRepository.GetCategoryPreferenceAsync("user-1", MemoryCategory.UserPreference, Arg.Any<CancellationToken>()).Returns(categoryPreference);

        var handler = new CreateMemoryCandidateCommandHandler(memoryRepository, preferenceRepository, approvalRepository, unitOfWork);
        var command = new CreateMemoryCandidateCommand("user-1", null, MemoryCategory.UserPreference, "Prefers dark mode.", 0.6m, 0.8m, false);

        var memoryId = await handler.Handle(command, CancellationToken.None);

        memoryId.Should().NotBeNull();
        memoryRepository.Received(1).Add(Arg.Is<Domain.Memory.Memory>(m => m != null && m.UserId == "user-1" && m.SourceType == MemorySourceType.AgentProposed));
        approvalRepository.Received(1).Add(Arg.Any<MemoryApproval>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateMemoryCandidateCommandHandler_ShouldCreateNothing_WhenTheCategoryIsDisabledForThatUser()
    {
        var memoryRepository = Substitute.For<IMemoryRepository>();
        var preferenceRepository = Substitute.For<IMemoryPreferenceRepository>();
        var approvalRepository = Substitute.For<IMemoryApprovalRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var disabledPreference = MemoryCategoryPreference.CreateDefault("user-1", MemoryCategory.UserPreference, "system");
        disabledPreference.Update(MemoryApprovalMode.Disabled, isEnabled: null, "user-1");
        preferenceRepository.GetCategoryPreferenceAsync("user-1", MemoryCategory.UserPreference, Arg.Any<CancellationToken>()).Returns(disabledPreference);

        var handler = new CreateMemoryCandidateCommandHandler(memoryRepository, preferenceRepository, approvalRepository, unitOfWork);
        var command = new CreateMemoryCandidateCommand("user-1", null, MemoryCategory.UserPreference, "Prefers dark mode.", 0.6m, 0.8m, false);

        var memoryId = await handler.Handle(command, CancellationToken.None);

        memoryId.Should().BeNull();
        memoryRepository.DidNotReceive().Add(Arg.Any<Domain.Memory.Memory>());
    }
}
